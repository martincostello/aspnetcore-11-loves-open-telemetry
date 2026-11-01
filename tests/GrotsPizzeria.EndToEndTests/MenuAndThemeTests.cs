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
            await Expect(region.Locator("article.pizza-card", new() { HasText = item })).ToBeVisibleAsync();
        }
    }

    [Fact]
    public async Task Theme_Follows_The_System_By_Default_And_Can_Be_Overridden()
    {
        await Page.GotoAsync("/");

        // Light system preference (see ColorScheme) -> light theme, with "System" selected
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        await Expect(Page.GetByLabel("Theme")).ToHaveValueAsync("system");

        // The system changes its mind and the page follows
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        // An explicit choice wins, and sticks across reloads
        await Page.GetByLabel("Theme").SelectOptionAsync("light");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");

        await Page.ReloadAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        await Expect(Page.GetByLabel("Theme")).ToHaveValueAsync("light");
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
