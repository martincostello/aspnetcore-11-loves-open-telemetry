using System.Text.Json.Nodes;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using System.Text.RegularExpressions;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>
/// Special offers: every 5 minutes an item in each section of the menu goes 15% off. Which ones is random, and the stack
/// the tests run against has the offers switched off (they would change the totals other tests check), so these tests
/// give the browser a menu with offers of their own. The menu service has its own tests of choosing them.
/// </summary>
public sealed class OffersTests(PizzeriaFixture fixture) : PizzeriaTest(fixture)
{
    private int _menuRequests;

    /// <summary>Replaces the menu the browser gets with one where the items <paramref name="onOffer"/> picks have the offer.</summary>
    private async Task OfferAsync(Func<int, (string Id, DateTimeOffset EndsAt)[]> onOffer) =>
        await Page.RouteAsync("**/api/menu", async route =>
        {
            var response = await route.FetchAsync();
            var menu = JsonNode.Parse(await response.TextAsync())!.AsArray();
            var offers = onOffer(Interlocked.Increment(ref _menuRequests));

            foreach (var item in menu)
            {
                var offer = offers.FirstOrDefault(o => o.Id == (string?)item!["id"]);

                if (offer.Id is not null)
                {
                    var price = (decimal)item!["price"]!;
                    item["offer"] = new JsonObject
                    {
                        ["discount"] = 0.15,
                        ["price"] = Math.Round(price * 0.85m, 2, MidpointRounding.AwayFromZero),
                        ["endsAt"] = offer.EndsAt.ToString("O"),
                    };
                }
            }

            await route.FulfillAsync(new() { Response = response, Body = menu.ToJsonString() });
        });

    [Fact]
    public async Task Items_On_Special_Offer_Show_Their_Offer_Price()
    {
        await OfferAsync(_ => [("margherita", DateTimeOffset.UtcNow.AddMinutes(5)), ("garlic-bread", DateTimeOffset.UtcNow.AddMinutes(5))]);
        await Page.GotoAsync("/menu");

        // Only the items on offer say so, and show what they cost with 15% off (9.50 becomes 8.08)
        var margherita = Page.GetByTestId("menu-item").Filter(new() { HasText = "Margherita" });
        await Expect(margherita).ToContainTextAsync("Special offer");
        await Expect(margherita).ToContainTextAsync("£9.50");
        await Expect(margherita).ToContainTextAsync("£8.08");
        await Expect(margherita).ToContainTextAsync("15% off");
        await Expect(Page.GetByTestId("menu-item").Filter(new() { HasText = "Special offer" })).ToHaveCountAsync(2);

        var marinara = Page.GetByTestId("menu-item").Filter(new() { HasText = "Marinara" });
        await Expect(marinara).Not.ToContainTextAsync("Special offer");
        await Expect(marinara).ToContainTextAsync("£8.50");
    }

    [Fact]
    public async Task Cart_Uses_The_Offer_Price_And_Follows_The_Offers_When_They_Change()
    {
        // The first offers end a few seconds after the menu is first loaded (long enough for the test to look at them); the next ones are on a different item
        await OfferAsync(call => call == 1
            ? [("margherita", DateTimeOffset.UtcNow.AddSeconds(8))]
            : [("marinara", DateTimeOffset.UtcNow.AddMinutes(5))]);

        await SignInAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" })).ToBeVisibleAsync();
        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Menu" }).ClickAsync();

        var margherita = Page.GetByTestId("menu-item").Filter(new() { HasText = "Margherita" });
        var marinara = Page.GetByTestId("menu-item").Filter(new() { HasText = "Marinara" });
        await Expect(margherita).ToHaveClassAsync(new Regex("on-offer"));

        await AddToCartAsync("Margherita", times: 2);
        await Page.GetByTestId("cart-link").ClickAsync();
        await Expect(Page.GetByTestId("totals")).ToContainTextAsync("£16.16"); // 2 x £8.08
        await Expect(Page.GetByTestId("cart-line")).ToContainTextAsync("Special offer");

        // When the offers end the menu is fetched again and the cart is priced at what will actually be charged
        await Expect(Page.GetByTestId("totals")).ToContainTextAsync("£19.00", new() { Timeout = 20_000 }); // 2 x £9.50
        await Expect(Page.GetByTestId("cart-line")).Not.ToContainTextAsync("Special offer");

        await Page.GetByTestId("sidebar").GetByRole(AriaRole.Link, new() { Name = "Menu" }).ClickAsync();
        await Expect(margherita).Not.ToHaveClassAsync(new Regex("on-offer"));
        await Expect(marinara).ToHaveClassAsync(new Regex("on-offer"));
        await Expect(Page.GetByTestId("menu-item").Filter(new() { Has = Page.GetByTestId("special-offer") })).ToHaveCountAsync(1);
    }


    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
