using System.Text.RegularExpressions;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>
/// Runs axe-core (WCAG 2.2 A and AA rules) over every page, in both themes, signed out and signed in.
/// </summary>
public sealed class AccessibilityTests(PizzeriaFixture fixture) : PizzeriaTest(fixture)
{
    private static readonly string[] Standards = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"];

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task Public_Pages_Have_No_Accessibility_Violations(string theme)
    {
        await Page.GotoAsync("/");
        await Page.GetByLabel("Theme").SelectOptionAsync(theme);
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", theme);

        foreach (var path in new[] { "/", "/menu", "/login", "/register", "/does-not-exist" })
        {
            await Page.GotoAsync(path);
            await Expect(Page.Locator("main h1")).ToBeVisibleAsync();

            if (path == "/menu")
            {
                await Expect(Page.Locator("article.pizza-card").First).ToBeVisibleAsync();
            }

            await AssertNoViolationsAsync($"{path} ({theme})");
        }
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task Signed_In_Pages_Have_No_Accessibility_Violations(string theme)
    {
        await Page.GotoAsync("/");
        await Page.GetByLabel("Theme").SelectOptionAsync(theme);
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", theme);

        await SignInAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();

        await Page.Locator("nav.sidebar").GetByRole(AriaRole.Link, new() { Name = "Menu" }).ClickAsync();
        await AddToCartAsync("Margherita", times: 2);
        await AddToCartAsync("Garlic Bread");

        await Page.Locator("nav.sidebar").GetByRole(AriaRole.Link, new() { Name = "Cart" }).ClickAsync();
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Your cart");
        await AssertNoViolationsAsync($"/cart ({theme})");

        // And with validation messages and the error banner showing
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Place order") }).ClickAsync();
        await Expect(Page.Locator(".validation-message").First).ToBeVisibleAsync();
        await AssertNoViolationsAsync($"/cart with validation errors ({theme})");

        await Page.GetByLabel("Delivery address").FillAsync("1 Loki Lane, Tempo Town");
        await Page.GetByLabel("Last 4 digits of your card").FillAsync("4242");
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Place order") }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/orders/ord_[0-9a-f]+$"));
        await Expect(Page.Locator("ol.steps li")).ToHaveCountAsync(5);
        await AssertNoViolationsAsync($"order details ({theme})");

        await Page.Locator("nav.sidebar").GetByRole(AriaRole.Link, new() { Name = "My orders" }).ClickAsync();
        await Expect(Page.Locator("table.table")).ToBeVisibleAsync();
        await AssertNoViolationsAsync($"/orders ({theme})");
    }

    private async Task AssertNoViolationsAsync(string what)
    {
        var result = await Page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Standards] },
        });

        var report = string.Join(
            Environment.NewLine,
            result.Violations.Select(v =>
                $"  [{v.Impact}] {v.Id}: {v.Help} ({v.HelpUrl}){Environment.NewLine}" +
                string.Join(Environment.NewLine, v.Nodes.Take(5).Select(n => $"      {string.Join(" ", n.Target)}  {n.Html}"))));

        Assert.True(result.Violations.Length == 0, $"Accessibility violations on {what}:{Environment.NewLine}{report}");
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
