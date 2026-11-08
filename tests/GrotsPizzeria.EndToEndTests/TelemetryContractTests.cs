using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using GrotsPizzeria.EndToEndTests.Backend;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>
/// The central promise of the sample: a request that starts in the browser is one trace through every application that it passes
/// through, whatever language they are in, with the context that the browser chose (baggage) available at each step, and the same
/// whether it worked, was refused or failed. These look at the traces where the applications sent them (the backend of the
/// Docker Compose stack), not just at whether they were sent, so they need that stack: <c>E2E_BASE_URL</c> and <c>E2E_GRAFANA_URL</c>.
/// <para>
/// The routes are the ones the applications report (<c>http.route</c>: the template of the route and not the URL).
/// </para>
/// </summary>
public sealed class TelemetryContractTests(PizzeriaFixture fixture) : PizzeriaTest(fixture)
{
    private const string Action = "contract-test";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SettleForWhatIsNotThere = TimeSpan.FromSeconds(8);

    // The server span that each application has for an order (the route that it reports), in the order that they are called
    private static readonly (string Service, string Route)[] ServersUpToThePayment =
    [
        ("grots-web", "/api/orders/"),
        ("grots-orders", "/orders"),
        ("grots-menu", "/menu/price"),
        ("grots-checkout", "/checkout"),
    ];

    private static readonly (string Service, string Route)[] AllServers = [.. ServersUpToThePayment, ("grots-delivery", "/deliveries")];

    [Fact]
    public async Task An_Order_That_Is_Placed_In_The_Browser_Is_One_Trace_Through_Every_Application()
    {
        var grafana = RequireBackend();

        await SignInAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();

        string? traceParent = null;
        Page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.EndsWith("/api/orders", StringComparison.Ordinal))
            {
                traceParent = request.Headers["traceparent"];
            }
        };

        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Menu" }).ClickAsync();
        await AddToCartAsync("Garlic Bread");
        await PlaceOrderAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/orders/ord_[0-9a-f]+$"));

        // The browser sends its spans soon after they end, and what is left when the page is left: leave it a moment, as a person would
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await Page.GotoAsync("about:blank");

        Assert.NotNull(traceParent);
        var traceId = traceParent.Split('-')[1];

        var trace = await grafana.WaitForTraceAsync(
            traceId,
            t => t.IsComplete(AllServers) &&
                t.Roots.All(r => r.Service == "grots-ui") &&
                t.Spans.Any(x => x.Service == "grots-ui" && x.Name == "place order"),
            Timeout,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);

        // The trace is the one that the browser started: it starts there (with what Blazor made of the click), and the BFF is further down it
        var root = Assert.Single(trace.Roots);
        Assert.Equal("grots-ui", root.Service);

        var web = trace.Server("grots-web", "/api/orders/");
        AssertCalledBy(trace, web, "grots-ui");
        Assert.Equal(traceId, web.TraceId);

        var above = trace.Ancestors(web);
        Assert.Contains(root, above);
        Assert.Contains(above, s => s.Service == "grots-ui" && s.Name == "place order");

        AssertOrderWasPassedOn(trace, web, expectedStatus: 201, "place-order");
        Assert.DoesNotContain(trace.Spans, s => s.IsError);
    }

    [Fact]
    public async Task An_Order_That_Is_Placed_Is_One_Trace_Through_Every_Application_With_What_The_Caller_Said()
    {
        var grafana = RequireBackend();
        using var client = await SignedInClientAsync();

        var (response, sent) = await PlaceOrderThroughTheApiAsync(client, "4242");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var trace = await grafana.WaitForTraceAsync(sent.TraceId, t => t.IsComplete(AllServers), Timeout, TimeSpan.Zero, TestContext.Current.CancellationToken);

        // The BFF carried on the trace that the caller sent, as a child of the span that the caller said it was in
        var web = trace.Server("grots-web", "/api/orders/");
        Assert.Equal(sent.SpanId, web.ParentSpanId);

        AssertOrderWasPassedOn(trace, web, expectedStatus: 201, Action);
        Assert.DoesNotContain(trace.Spans, s => s.IsError);
    }

    [Fact]
    public async Task A_Declined_Payment_Is_A_Refusal_For_The_Server_And_A_Failure_For_The_Caller()
    {
        var grafana = RequireBackend();
        using var client = await SignedInClientAsync();

        var (response, sent) = await PlaceOrderThroughTheApiAsync(client, "0000");
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);

        var trace = await grafana.WaitForTraceAsync(sent.TraceId, t => t.IsComplete(ServersUpToThePayment), Timeout, SettleForWhatIsNotThere, TestContext.Current.CancellationToken);

        // The delivery was never started: the payment came before it
        Assert.DoesNotContain("grots-delivery", trace.Services);

        var web = trace.Server("grots-web", "/api/orders/");
        var orders = trace.Server("grots-orders", "/orders");
        var checkout = trace.Server("grots-checkout", "/checkout");

        Assert.Equal(sent.SpanId, web.ParentSpanId);
        AssertCalledBy(trace, orders, "grots-web");
        AssertCalledBy(trace, checkout, "grots-orders");

        // A 4xx is not an error to the server that answered with it: only a 5xx makes a server span an error
        AssertStatus(web, 402, error: false);
        AssertStatus(orders, 402, error: false);
        AssertStatus(checkout, 402, error: false);

        // ...but it is one to the client that was refused: that is how the conventions treat the two sides
        AssertStatus(trace.Client("grots-orders", "/checkout"), 402, error: true);
        AssertStatus(trace.Client("grots-web", "/orders"), 402, error: true);

        AssertBaggage(Action, web, orders, checkout, trace.Server("grots-menu", "/menu/price"));
    }

    [Fact]
    public async Task A_Payment_Gateway_That_Is_Down_Is_A_Failure_For_Every_Application_That_Passes_It_On()
    {
        var grafana = RequireBackend();
        using var client = await SignedInClientAsync();

        var (response, sent) = await PlaceOrderThroughTheApiAsync(client, "0503");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var trace = await grafana.WaitForTraceAsync(sent.TraceId, t => t.IsComplete(ServersUpToThePayment), Timeout, SettleForWhatIsNotThere, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("grots-delivery", trace.Services);

        var web = trace.Server("grots-web", "/api/orders/");
        var orders = trace.Server("grots-orders", "/orders");
        var checkout = trace.Server("grots-checkout", "/checkout");

        Assert.Equal(sent.SpanId, web.ParentSpanId);
        AssertCalledBy(trace, orders, "grots-web");
        AssertCalledBy(trace, checkout, "grots-orders");

        // Each of them failed, and says so: the one that was down (503), and the ones that could not do what they were asked because of it (502)
        AssertStatus(checkout, 503, error: true);
        AssertStatus(orders, 502, error: true);
        AssertStatus(web, 502, error: true);
        AssertStatus(trace.Client("grots-orders", "/checkout"), 503, error: true);
        AssertStatus(trace.Client("grots-web", "/orders"), 502, error: true);

        AssertBaggage(Action, web, orders, checkout, trace.Server("grots-menu", "/menu/price"));
    }

    [Fact]
    public async Task Health_Checks_Are_Not_Recorded_By_Any_Application()
    {
        var grafana = RequireBackend();
        var cancellationToken = TestContext.Current.CancellationToken;

        // The stack has been healthy for a while, which means that its health checks have been made
        var end = DateTimeOffset.UtcNow;
        var start = end.AddMinutes(-10);

        var spans = await grafana.QueryAsync(
            "tempo",
            $"/api/search?q={Uri.EscapeDataString("{ span.http.route = \"/healthz\" || span.url.path = \"/healthz\" }")}&limit=5&start={start.ToUnixTimeSeconds()}&end={end.ToUnixTimeSeconds()}",
            cancellationToken);

        // A missing endpoint is not an empty answer: the search has to have been answered (with no matches, as it is expected to be)
        Assert.NotNull(spans);
        Assert.Empty(spans["traces"]?.AsArray() ?? []);

        var metrics = await grafana.QueryAsync(
            "prometheus",
            $"/api/v1/query?query={Uri.EscapeDataString("sum by (service_name) (increase(http_server_request_duration_seconds_count{http_route=\"/healthz\"}[3m]))")}",
            cancellationToken);

        var counted = metrics?["data"]?["result"]?.AsArray();
        Assert.NotNull(counted);

        foreach (var series in counted)
        {
            Assert.True(double.Parse((string)series!["value"]![1]!, System.Globalization.CultureInfo.InvariantCulture) == 0, $"{series["metric"]} counted health checks.");
        }
    }

    /// <summary>
    /// The calls of an order that was placed: each application was called by the one before it, with the status that it answered with,
    /// the method and the route that it served (and the destination that the caller called), and what the caller of the BFF said.
    /// </summary>
    private static void AssertOrderWasPassedOn(TraceData trace, TraceSpan web, int expectedStatus, string action)
    {
        var orders = trace.Server("grots-orders", "/orders");
        var menu = trace.Server("grots-menu", "/menu/price");
        var checkout = trace.Server("grots-checkout", "/checkout");
        var delivery = trace.Server("grots-delivery", "/deliveries");

        AssertCalledBy(trace, orders, "grots-web");
        AssertCalledBy(trace, menu, "grots-orders");
        AssertCalledBy(trace, checkout, "grots-orders");
        AssertCalledBy(trace, delivery, "grots-orders");

        AssertStatus(web, expectedStatus, error: false);
        AssertStatus(orders, expectedStatus, error: false);
        AssertStatus(menu, 200, error: false);
        AssertStatus(checkout, 201, error: false);
        AssertStatus(delivery, 201, error: false);

        // The calls that were made are the same ones, seen from the other side
        AssertStatus(trace.Client("grots-web", "/orders"), expectedStatus, error: false);
        AssertStatus(trace.Client("grots-orders", "/menu/price"), 200, error: false);
        AssertStatus(trace.Client("grots-orders", "/checkout"), 201, error: false);
        AssertStatus(trace.Client("grots-orders", "/deliveries"), 201, error: false);

        foreach (var server in new[] { web, orders, menu, checkout, delivery })
        {
            Assert.Equal("POST", server.Attribute("http.request.method"));
        }

        AssertBaggage(action, web, orders, menu, checkout, delivery);
    }

    /// <summary>The span was started by a call from the application, which is what the trace says: its parent is a client span of it.</summary>
    private static void AssertCalledBy(TraceData trace, TraceSpan server, string caller)
    {
        var parent = trace.ParentOf(server);

        Assert.True(parent is not null, $"{server} has no parent in the trace.{Environment.NewLine}{trace.Describe()}");
        Assert.True(parent.IsClient && parent.Service == caller, $"{server} was not called by a client span of {caller}, but by {parent}.");
        Assert.False(string.IsNullOrEmpty(parent.Attribute("server.address")), $"{parent} does not say what it called (server.address).");
    }

    private static void AssertStatus(TraceSpan span, int httpStatus, bool error)
    {
        Assert.Equal(httpStatus.ToString(System.Globalization.CultureInfo.InvariantCulture), span.Attribute("http.response.status_code"));
        Assert.True(span.IsError == error, $"{span} should {(error ? string.Empty : "not ")}have a status of Error.");
    }

    private static void AssertBaggage(string expected, params TraceSpan[] spans)
    {
        foreach (var span in spans)
        {
            Assert.True(span.Attribute("grot.ui.action") == expected, $"{span} has not got what the caller said in its baggage ({expected}), but {span.Attribute("grot.ui.action") ?? "nothing"}.");
        }
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private GrafanaClient RequireBackend()
    {
        Assert.SkipUnless(Grafana is not null, "This looks at the telemetry in the backend of a running Docker Compose stack: set E2E_BASE_URL and E2E_GRAFANA_URL (see the README).");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("E2E_BASE_URL") is { Length: > 0 }, "This needs the stack that E2E_GRAFANA_URL is the backend of: set E2E_BASE_URL.");
        return Grafana!;
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = new HttpClient { BaseAddress = BaseAddress };
        var name = $"contract-{Guid.NewGuid():N}"[..16];

        var response = await client.PostAsJsonAsync("/api/auth/register", new { username = name, displayName = name, password = "Pizza!Passw0rd" }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return client;
    }

    /// <summary>Places an order in a trace that this chose, in a span that this made up, with some baggage, so that the trace can be found.</summary>
    private static async Task<(HttpResponseMessage Response, TestTrace Trace)> PlaceOrderThroughTheApiAsync(HttpClient client, string cardLast4)
    {
        var trace = TestTrace.New();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new
            {
                address = "1 Loki Lane, Tempo Town",
                items = new[] { new { pizzaId = "margherita", quantity = 1 } },
                payment = new { method = "card", cardLast4 },
            }),
        };
        request.Headers.Add("traceparent", trace.TraceParent);
        request.Headers.Add("baggage", $"grot.ui.action={Action}");

        return (await client.SendAsync(request, TestContext.Current.CancellationToken), trace);
    }
}
