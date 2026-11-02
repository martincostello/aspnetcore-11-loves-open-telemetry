using System.Text.RegularExpressions;

namespace GrotsPizzeria.EndToEndTests;

[Collection(PizzeriaCollection.Name)]
public abstract class PizzeriaTest(PizzeriaFixture fixture) : IAsyncLifetime
{
    public const string DemoUsername = "grot";
    public const string DemoPassword = "Grot!Pizza1";

    private IBrowserContext _context = default!;

    protected IPage Page { get; private set; } = default!;

    protected virtual ColorScheme ColorScheme => ColorScheme.Light;

    public async ValueTask InitializeAsync()
    {
        _context = await fixture.Browser.NewContextAsync(new()
        {
            BaseURL = fixture.BaseAddress.ToString(),
            ColorScheme = ColorScheme,
            Locale = "en-US",
            Permissions = ["clipboard-read", "clipboard-write"],
            ViewportSize = new() { Width = 1280, Height = 900 },
        });

        Page = await _context.NewPageAsync();
        Page.Console += (_, m) => { if (m.Type is "error" or "warning") Console.WriteLine($"[browser {m.Type}] {m.Text}"); };
        Page.PageError += (_, e) => Console.WriteLine($"[browser pageerror] {e}");
    }

    protected OtlpSink? Sink => fixture.OtlpSink;

    protected Uri BaseAddress => fixture.BaseAddress;

    public async ValueTask DisposeAsync()
    {
        // Destroying a page outright gives it no chance to send the telemetry it has queued (the browser only does that
        // as the page is left), which leaves traces without their root spans in the backend. Leave it the way a user would.
        try
        {
            await Page.GotoAsync("about:blank");
        }
        catch (PlaywrightException)
        {
            // already gone
        }

        await _context.DisposeAsync();
    }

    protected async Task SignInAsync(string username = DemoUsername, string password = DemoPassword, string? returnTo = null)
    {
        await Page.GotoAsync(returnTo is null ? "/login" : $"/login?returnUrl={Uri.EscapeDataString(returnTo)}");
        await Page.GetByLabel("Username").FillAsync(username);
        await Page.GetByLabel("Password").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }

    protected async Task AddToCartAsync(string pizza, int times = 1)
    {
        var card = Page.GetByTestId("menu-item").Filter(new() { HasText = pizza });

        for (var i = 0; i < times; i++)
        {
            await card.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Add to cart") }).ClickAsync();
        }
    }

    protected async Task PlaceOrderAsync(string cardLast4 = "4242")
    {
        // Navigate within the app: a full page load would empty the (in-memory) cart.
        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Cart" }).ClickAsync();
        await Page.GetByLabel("Delivery address").FillAsync("1 Loki Lane, Tempo Town");
        await Page.GetByLabel("Last 4 digits of your card").FillAsync(cardLast4);
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Place order") }).ClickAsync();
    }
}
