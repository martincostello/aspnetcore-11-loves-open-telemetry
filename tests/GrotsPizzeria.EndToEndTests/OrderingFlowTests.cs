using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GrotsPizzeria.EndToEndTests;

public sealed class OrderingFlowTests(PizzeriaFixture fixture) : PizzeriaTest(fixture)
{
    [Fact]
    public async Task Can_Order_A_Pizza_And_Track_Its_Delivery()
    {
        // Sign in with the seeded demo account
        await SignInAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();

        // Pick two Margheritas, a Grot Special and a side of garlic bread from the menu (served by the Node.js service)
        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Menu" }).ClickAsync();
        await AddToCartAsync("Margherita", times: 2);
        await AddToCartAsync("Grot Special");
        await AddToCartAsync("Garlic Bread");
        await Expect(Page.GetByTestId("cart-count")).ToHaveTextAsync("4");

        // Pay (Java orders -> Python checkout -> Go delivery)
        await PlaceOrderAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/orders/ord_[0-9a-f]+$"));

        // The receipt adds up: 2 x 9.50 + 14.00 + 4.50 = 37.50, plus tax at the rate the receipt says it was charged at
        // (whatever the checkout service is configured with)
        await Expect(Page.GetByTestId("page-title")).ToContainTextAsync("Order ord_");
        await Expect(Page.GetByTestId("totals")).ToContainTextAsync(Money(37.50m));

        // (The page rounds the rate to show it, so the exact one is read from the order itself)
        var orderId = Regex.Match(Page.Url, "ord_[0-9a-f]+").Value;
        var order = JsonNode.Parse(await (await Page.Context.APIRequest.GetAsync($"/api/orders/{orderId}")).TextAsync())!;
        var rate = (decimal)order["taxRate"]!;
        var tax = Math.Round(37.50m * rate, 2, MidpointRounding.AwayFromZero);
        var total = 37.50m + tax;
        await Expect(Page.GetByTestId("totals")).ToContainTextAsync(Money(tax));
        await Expect(Page.GetByTestId("totals")).ToContainTextAsync(Money(total));

        // ...and the delivery is being tracked
        await Expect(Page.GetByTestId("delivery-step").First).ToContainTextAsync("Preparing your order");

        // The order shows up in the history
        await Page.GetByRole(AriaRole.Link, new() { Name = "My orders" }).ClickAsync();
        await Expect(Page.GetByTestId("order-row").First).ToContainTextAsync(Money(total));
    }

    [Fact]
    public async Task Declined_Payment_Is_Reported_And_The_Cart_Is_Kept()
    {
        await SignInAsync();

        // Wait for the sign-in to finish (it redirects home) before navigating on, or the redirect wins the race.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();

        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Menu" }).ClickAsync();
        await AddToCartAsync("Pepperoni");

        await PlaceOrderAsync(cardLast4: "0000");

        await Expect(Page.GetByTestId("error-alert")).ToContainTextAsync("Payment declined");
        await Expect(Page).ToHaveURLAsync(new Regex("/cart$"));
        await Expect(Page.GetByTestId("cart-count")).ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Browser_Sends_Its_Own_Telemetry_And_Continues_The_Trace_On_The_Server()
    {
        // The browser exports straight to the collector (no proxy), cross-origin, so CORS has to allow it
        // ...and the batch that holds the "place order" span is what we wait for (the SDK flushes every few seconds)
        var marker = System.Text.Encoding.UTF8.GetBytes("place order");
        var traces = Page.WaitForResponseAsync(
            response => response.Request.Method == "POST"
                && response.Url.EndsWith("/v1/traces", StringComparison.Ordinal)
                && response.Request.PostDataBuffer is { } body
                && body.AsSpan().IndexOf(marker) >= 0,
            new() { Timeout = 30_000 });

        await SignInAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();
        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Menu" }).ClickAsync();
        await AddToCartAsync("Garlic Bread");

        // Placing an order is one trace: the click, the browser's HTTP call, then the BFF's work onwards
        var order = Page.WaitForRequestAsync(request => request.Method == "POST" && request.Url.EndsWith("/api/orders", StringComparison.Ordinal));
        await PlaceOrderAsync();
        var headers = await (await order).AllHeadersAsync();
        var traceparent = headers["traceparent"];
        Assert.True(Regex.IsMatch(traceparent, "^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$"), "Unexpected traceparent: " + traceparent);

        var response = await traces;
        Assert.True(response.Ok, $"The collector rejected the browser's traces: {response.Status}");
        Assert.Equal("application/x-protobuf", (await response.Request.AllHeadersAsync())["content-type"]);
    }

    [Fact]
    public async Task Everything_Of_An_Item_Can_Be_Removed_From_The_Cart_In_One_Click()
    {
        await SignInAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();
        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Menu" }).ClickAsync();
        await AddToCartAsync("Margherita", times: 3);
        await AddToCartAsync("Grot Special");
        await AddToCartAsync("Garlic Bread", times: 2);
        await Expect(Page.GetByTestId("cart-count")).ToHaveTextAsync("6");
        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Cart" }).ClickAsync();

        var rows = Page.GetByTestId("cart-line");
        await Expect(rows).ToHaveCountAsync(3);

        // One press takes all three Margheritas away
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove all Margherita from your cart" }).ClickAsync();
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(rows.Filter(new() { HasText = "Margherita" })).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("totals")).ToContainTextAsync(Money(23.00m)); // Grot Special 14.00 + 2 x Garlic Bread 4.50
        await Expect(Page.GetByTestId("cart-count")).ToHaveTextAsync("3");

        // ...and the rest, one at a time, until the cart is empty
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove all Grot Special from your cart" }).ClickAsync();
        await Expect(rows).ToHaveCountAsync(1);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove all Garlic Bread from your cart" }).ClickAsync();
        await Expect(Page.GetByText("Your cart is empty.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Telemetry_Is_Sent_Promptly_So_Leaving_The_Page_Does_Not_Lose_The_Roots_Of_Traces()
    {
        Assert.SkipUnless(Sink is not null, "This needs the in-process OTLP sink, which is only used when the tests start the stack themselves.");

        await SignInAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();
        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Menu" }).ClickAsync();
        await AddToCartAsync("Garlic Bread");
        await PlaceOrderAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/orders/ord_[0-9a-f]+$"));

        // Leave soon afterwards, long before the exporter's old 5 second timer would have sent the "place order" span. The BFF has
        // already sent its spans (children of the browser's), so if the browser's were lost the backend would show them without a root.
        await Task.Delay(600, TestContext.Current.CancellationToken);
        await Page.GotoAsync("about:blank");

        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!Sink!.Contains("traces", "place order") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(Sink.Contains("traces", "place order"), "The browser's spans were not sent before the page was left.");
    }

    [Fact]
    public async Task Cart_Requires_Signing_In_And_Returns_You_Afterwards()
    {
        await Page.GotoAsync("/cart");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/login\?returnUrl=cart"));

        await Page.GetByLabel("Username").FillAsync(DemoUsername);
        await Page.GetByLabel("Password").FillAsync(DemoPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/cart$"));
        await Expect(Page.GetByTestId("page-title")).ToHaveTextAsync("Your cart");
    }

    [Theory]
    [InlineData("javascript:window.hijacked=true")]
    [InlineData("//example.com/phish")]
    [InlineData("https://example.com/phish")]
    [InlineData(@"/\example.com/phish")]
    [InlineData("http://[")]
    public async Task Signing_In_Never_Goes_On_To_Anywhere_Outside_The_App(string returnTo)
    {
        await SignInAsync(returnTo: returnTo);

        // They are signed in, and still in the app (at its home page), with nothing from the link having run
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();
        Assert.StartsWith(BaseAddress.ToString(), Page.Url);
        Assert.Null(await Page.EvaluateAsync<bool?>("window.hijacked"));
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
