using System.Net;
using System.Text.Json.Nodes;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>
/// The BFF documents its API with an OpenAPI document and a page (Swagger UI), which anyone may see without signing in. (The page
/// loads Swagger UI itself from a CDN, so these check what the BFF serves, not how a browser draws it.)
/// </summary>
public sealed class ApiDocumentationTests(PizzeriaFixture fixture) : PizzeriaTest(fixture)
{
    [Fact]
    public async Task Documentation_Page_Is_Served_Without_Signing_In()
    {
        using var client = NewClient();

        // The root of the BFF is the UI, so the page has its own address
        var redirect = await client.GetAsync("/swagger", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/swagger-ui/index.html", redirect.Headers.Location?.OriginalString);

        var page = await client.GetAsync("/swagger-ui/index.html", TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<title>grots-web API", html);
        Assert.Contains("data-spec-url=\"/openapi.json\"", html);

        foreach (var asset in new[] { "theme.css", "theme.js", "init.js", "grot.svg" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/swagger-ui/{asset}", TestContext.Current.CancellationToken)).StatusCode);
        }
    }

    [Fact]
    public async Task Openapi_Document_Describes_The_Api_Without_Signing_In()
    {
        using var client = NewClient();

        var response = await client.GetAsync("/openapi.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.StartsWith("3.1", (string)document["openapi"]!); // what the documentation page can show
        Assert.Equal("grots-web", (string)document["info"]!["title"]!);

        var paths = document["paths"]!.AsObject().Select(p => p.Key).ToHashSet();

        // The document has at least these (it has others too, such as /api/auth/me)
        foreach (var expected in new[] { "/api/menu", "/api/orders", "/api/orders/{id}", "/api/orders/{id}/tracking", "/api/auth/login", "/api/auth/register", "/api/config", "/healthz" })
        {
            Assert.Contains(expected, paths);
        }

        Assert.DoesNotContain(paths, p => p.StartsWith("/swagger", StringComparison.Ordinal) || p.StartsWith("/openapi", StringComparison.Ordinal));

        // Placing an order needs the cookie that signing in sets, and the menu does not
        Assert.Equal("cookie", (string)document["components"]!["securitySchemes"]!["cookieAuth"]!["in"]!);
        Assert.NotNull(document["paths"]!["/api/orders"]!["post"]!["security"]);
        Assert.Null(document["paths"]!["/api/menu"]!["get"]!["security"]);
        Assert.NotNull(document["components"]!["schemas"]!["PlaceOrderRequest"]);
    }

    [Fact]
    public async Task The_Api_Itself_Still_Needs_Signing_In()
    {
        using var client = NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders", TestContext.Current.CancellationToken)).StatusCode);
    }

    private HttpClient NewClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = BaseAddress };
}
