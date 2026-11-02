using System.Text.Json.Nodes;

namespace GrotsPizzeria.EndToEndTests;

public sealed class MenuAndThemeTests(PizzeriaFixture fixture) : PizzeriaTest(fixture)
{
    [Fact]
    public async Task Menu_Lists_The_Pizzas_Sides_And_Desserts_From_The_Menu_Service()
    {
        await Page.GotoAsync("/menu");

        foreach (var (section, item) in new[] { ("Pizzas", "Grot Special"), ("Sides", "Garlic Bread"), ("Desserts", "Tiramisu") })
        {
            var region = Page.GetByRole(AriaRole.Region, new() { Name = section, Exact = true });
            await Expect(region.GetByTestId("menu-item").Filter(new() { HasText = item })).ToBeVisibleAsync();
        }
    }

    [Fact]
    public async Task Prices_Are_Shown_In_The_Currency_The_Menu_Says()
    {
        // The menu service prices things in pounds unless it is told otherwise, so tell the browser it did
        await Page.RouteAsync("**/api/menu", async route =>
        {
            var response = await route.FetchAsync();
            var menu = JsonNode.Parse(await response.TextAsync())!.AsArray();

            foreach (var item in menu)
            {
                item!["currency"] = "EUR";
            }

            await route.FulfillAsync(new() { Response = response, Body = menu.ToJsonString() });
        });

        await Page.GotoAsync("/menu");

        var margherita = Page.GetByTestId("menu-item").Filter(new() { HasText = "Margherita" });
        await Expect(margherita).ToContainTextAsync("€9.50");
        await Expect(margherita).Not.ToContainTextAsync("£");
    }

    [Fact]
    public async Task Theme_Follows_The_System_By_Default_And_Can_Be_Overridden()
    {
        await Page.GotoAsync("/");

        // Light system preference (see ColorScheme) -> light theme, with "System" selected
        await Expect(Page.GetByTestId("document")).ToHaveAttributeAsync("data-theme", "light");
        await Expect(Page.GetByLabel("Theme")).ToHaveValueAsync("system");

        // The system changes its mind and the page follows
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Expect(Page.GetByTestId("document")).ToHaveAttributeAsync("data-theme", "dark");

        // An explicit choice wins, and sticks across reloads
        await Page.GetByLabel("Theme").SelectOptionAsync("light");
        await Expect(Page.GetByTestId("document")).ToHaveAttributeAsync("data-theme", "light");

        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("document")).ToHaveAttributeAsync("data-theme", "light");
        await Expect(Page.GetByLabel("Theme")).ToHaveValueAsync("light");
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
