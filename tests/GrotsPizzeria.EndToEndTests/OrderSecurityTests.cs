using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>
/// Orders belong to the user who placed them, and no order can be paid for with a total below zero. These talk to the BFF's
/// API directly (as someone editing the URL or the request would), with a separate signed-in client for each user.
/// </summary>
public sealed class OrderSecurityTests(PizzeriaFixture fixture) : PizzeriaTest(fixture)
{
    private const string Password = "Pizza!Passw0rd";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_User_Can_Only_See_Their_Own_Orders()
    {
        using var alice = await NewUserAsync("alice");
        using var bob = await NewUserAsync("bob");
        using var anonymous = NewClient();

        var aliceOrder = await PlaceOrderAsync(alice);
        var bobOrder = await PlaceOrderAsync(bob);
        Assert.NotEqual(aliceOrder, bobOrder);

        // The owner sees the order, in their list and by its address, and where the delivery has got to
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/orders/{aliceOrder}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/orders/{aliceOrder}/tracking")).StatusCode);
        Assert.Equal(new[] { aliceOrder }, await OrderIdsAsync(alice));
        Assert.Equal(new[] { bobOrder }, await OrderIdsAsync(bob));

        // Someone else asking for it by id (the URL is the only thing they need to change) is told it is not there...
        var notThere = await bob.GetAsync("/api/orders/ord_000000000000");

        foreach (var path in new[] { $"/api/orders/{aliceOrder}", $"/api/orders/{aliceOrder}/tracking" })
        {
            var response = await bob.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(aliceOrder, body);
            Assert.DoesNotContain("Loki Lane", body);
            Assert.DoesNotContain("txn_", body);
        }

        // ... exactly as if it never existed, so ids cannot be probed to find out which ones are real
        Assert.Equal(HttpStatusCode.NotFound, notThere.StatusCode);
        Assert.Equal(
            await ProblemAsync(await bob.GetAsync("/api/orders/ord_000000000000")),
            await ProblemAsync(await bob.GetAsync($"/api/orders/{aliceOrder}")));

        // Variations on the id do not get round it
        foreach (var variation in new[]
        {
            aliceOrder.ToUpperInvariant(), $"{aliceOrder}/", $"{aliceOrder}%20", $"{aliceOrder}%00", $"%2e%2e/{aliceOrder}",
            $"{aliceOrder}?userId=anyone", $"{aliceOrder}%2F..%2F{bobOrder}",
        })
        {
            var response = await bob.GetAsync($"/api/orders/{variation}");
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("Loki Lane", body);
        }

        // The list is built from who is signed in; nothing in the request can change that
        foreach (var query in new[] { "?userId=alice", $"?userId={Uri.EscapeDataString(await UserIdOfAsync(alice, aliceOrder))}", "?user=*", "?all=true" })
        {
            var response = await bob.GetAsync($"/api/orders{query}");
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(aliceOrder, body);
        }

        // Nor can they be placed in someone else's name
        var bobsOrder = JsonNode.Parse(await bob.GetStringAsync($"/api/orders/{bobOrder}"))!;
        var forged = await bob.PostAsync("/api/orders", JsonBody(Order(), ("userId", await UserIdOfAsync(alice, aliceOrder)), ("userName", "alice")));
        Assert.Equal(HttpStatusCode.Created, forged.StatusCode);
        var forgedOrder = JsonNode.Parse(await forged.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal((string)bobsOrder["userId"]!, (string)forgedOrder["userId"]!);
        Assert.Equal((string)bobsOrder["userName"]!, (string)forgedOrder["userName"]!);
        Assert.DoesNotContain((string)forgedOrder["id"]!, await OrderIdsAsync(alice));

        // Signed out, there is nothing to see at all
        foreach (var path in new[] { "/api/orders", $"/api/orders/{aliceOrder}", $"/api/orders/{aliceOrder}/tracking" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/orders", JsonBody(Order()))).StatusCode);
    }

    [Fact]
    public async Task Another_Users_Order_Is_Not_Found_In_The_Browser_Either()
    {
        using var alice = await NewUserAsync("alice");
        var aliceOrder = await PlaceOrderAsync(alice);
        var bobName = $"bob-{Guid.NewGuid():N}"[..16];

        await Page.GotoAsync("/register");
        await Page.GetByLabel("Username").FillAsync(bobName);
        await Page.GetByLabel("Password").FillAsync(Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/menu$"));

        // Changing the order id in the URL to one that is someone else's
        await Page.GotoAsync($"/orders/{aliceOrder}");
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Order not found");

        var text = await Page.GetByTestId("content").InnerTextAsync();
        Assert.DoesNotContain("Loki Lane", text);
        Assert.DoesNotContain("txn_", text);
        Assert.DoesNotContain("£", text);

        // And it is not in their list of orders
        await Page.GotoAsync("/orders");
        await Expect(Page.GetByTestId("content")).Not.ToContainTextAsync(aliceOrder);
    }

    public static TheoryData<string, string> BadOrders() => new()
    {
        { "negative quantity", Items(("margherita", -1)) },
        { "negative quantity, far", Items(("margherita", int.MinValue)) },
        { "zero quantity", Items(("margherita", 0)) },
        { "too many of one item", Items(("margherita", 21)) },
        { "quantity beyond int", """{"items":[{"pizzaId":"margherita","quantity":3000000000}]}""" },
        { "fractional quantity", """{"items":[{"pizzaId":"margherita","quantity":1.5}]}""" },
        { "fractional negative quantity", """{"items":[{"pizzaId":"margherita","quantity":-0.5}]}""" },
        { "negative quantity as text", """{"items":[{"pizzaId":"margherita","quantity":"-2"}]}""" },
        { "quantity as words", """{"items":[{"pizzaId":"margherita","quantity":"minus two"}]}""" },
        { "quantity in exponent form", """{"items":[{"pizzaId":"margherita","quantity":-2e0}]}""" },
        { "no quantity", """{"items":[{"pizzaId":"margherita"}]}""" },
        { "null quantity", """{"items":[{"pizzaId":"margherita","quantity":null}]}""" },
        { "a negative line to cancel out a positive one", Items(("margherita", 5), ("margherita", -5)) },
        { "a negative line to cancel out a dearer one", Items(("grot-special", 20), ("marinara", -20)) },
        { "no items", Items() },
        { "null items", """{"items":null}""" },
        { "no pizza id", """{"items":[{"quantity":1}]}""" },
        { "unknown item", Items(("not-on-the-menu", 1)) },
        { "a null line", """{"items":[null]}""" },
        { "a null line among others", """{"items":[{"pizzaId":"margherita","quantity":1},null]}""" },
        { "too many lines", Items(Enumerable.Repeat(("margherita", 1), 51).ToArray()) },
    };

    [Theory]
    [MemberData(nameof(BadOrders))]
    public async Task Orders_With_Impossible_Quantities_Are_Refused_And_Nothing_Is_Charged(string what, string itemsJson)
    {
        using var user = await NewUserAsync("neg");
        var before = await OrderIdsAsync(user);

        var order = JsonNode.Parse(itemsJson)!.AsObject();
        order["address"] = "1 Loki Lane, Tempo Town";
        order["payment"] = new JsonObject { ["method"] = "cash" };

        var response = await user.PostAsync("/api/orders", new StringContent(order.ToJsonString(), Encoding.UTF8, "application/json"));

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"{what}: expected 400 but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        Assert.Equal(before, await OrderIdsAsync(user));
    }

    [Fact]
    public async Task Everything_On_The_Menu_Can_Be_Ordered_At_Once()
    {
        // The cart lets someone add every item on the menu, so the API has to take an order with a line for each
        using var user = await NewUserAsync("all");
        var menu = JsonNode.Parse(await user.GetStringAsync("/api/menu"))!.AsArray();
        var items = menu.Select(i => ((string)i!["id"]!, 1)).ToArray();
        Assert.True(items.Length > 20);

        var id = await PlaceOrderAsync(user, items);

        var order = JsonNode.Parse(await user.GetStringAsync($"/api/orders/{id}"))!;
        Assert.Equal(items.Length, order["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task An_Address_Written_In_Any_Script_Can_Be_Delivered_To()
    {
        // The longest address the BFF takes is 200 characters, which is far more than 256 bytes in most scripts
        using var user = await NewUserAsync("addr");
        var order = Order();
        order["address"] = new string('東', 200);

        var response = await user.PostAsync("/api/orders", JsonBody(order));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(await OrderIdsAsync(user));
    }

    [Fact]
    public async Task Orders_Are_Priced_By_The_Menu_Not_By_The_Request()
    {
        using var user = await NewUserAsync("price");
        var before = await OrderIdsAsync(user);

        // Prices, totals and tax in the request are not something a client gets to say; they are ignored
        var response = await user.PostAsync("/api/orders", JsonBody(
            Order(),
            ("price", -100), ("unitPrice", -100), ("subtotal", -100), ("tax", -100), ("total", -100), ("amount", -100), ("discount", 1)));
        var order = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var subtotal = (decimal)order["subtotal"]!;
        var tax = (decimal)order["tax"]!;
        var total = (decimal)order["total"]!;

        // There is something to pay for, and there may be no tax on it (a rate of 0 is valid)
        Assert.True(subtotal > 0 && tax >= 0 && total > 0, $"subtotal {subtotal}, tax {tax}, total {total}");
        Assert.Equal(subtotal + tax, total);

        foreach (var line in order["items"]!.AsArray())
        {
            Assert.True((decimal)line!["unitPrice"]! > 0);
            Assert.True((int)line["quantity"]! > 0);
            Assert.Equal((decimal)line["unitPrice"]! * (int)line["quantity"]!, (decimal)line["lineTotal"]!);
        }

        Assert.Equal(before.Count + 1, (await OrderIdsAsync(user)).Count);
    }

    [Fact]
    public async Task Every_Order_Ever_Placed_Has_A_Positive_Total()
    {
        // Whatever orders the other tests (and anyone using the sample) have left behind: none is for nothing or less
        using var user = await NewUserAsync("sum");
        await PlaceOrderAsync(user);
        await PlaceOrderAsync(user, ("grot-special", 20), ("garlic-bread", 20), ("tiramisu", 1));

        var orders = JsonNode.Parse(await user.GetStringAsync("/api/orders"))!.AsArray();
        Assert.Equal(2, orders.Count);
        Assert.All(orders, o => Assert.True((decimal)o!["total"]! > 0 && (decimal)o["subtotal"]! > 0));
    }

    private static string Items(params (string Id, int Quantity)[] items) =>
        new JsonObject
        {
            ["items"] = new JsonArray([.. items.Select(i => (JsonNode)new JsonObject { ["pizzaId"] = i.Id, ["quantity"] = i.Quantity })]),
        }.ToJsonString();

    private static JsonObject Order(params (string Id, int Quantity)[] items) =>
        new()
        {
            ["address"] = "1 Loki Lane, Tempo Town",
            ["items"] = new JsonArray([.. (items.Length > 0 ? items : [("margherita", 2), ("garlic-bread", 1)])
                .Select(i => (JsonNode)new JsonObject { ["pizzaId"] = i.Id, ["quantity"] = i.Quantity })]),
            ["payment"] = new JsonObject { ["method"] = "cash" },
        };

    /// <summary>The order with more properties added (or replaced), as a request body.</summary>
    private static StringContent JsonBody(JsonObject order, params (string Name, object Value)[] extra)
    {
        foreach (var (name, value) in extra)
        {
            order[name] = JsonSerializer.SerializeToNode(value);
        }

        return new StringContent(order.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private TestApi NewClient() =>
        new(new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = BaseAddress });

    /// <summary>A client signed in as a new user (each has their own cookie, so each is a different person).</summary>
    private async Task<TestApi> NewUserAsync(string prefix)
    {
        var client = NewClient();
        var username = $"{prefix}-{Guid.NewGuid():N}"[..16];

        var response = await client.PostAsJsonAsync("/api/auth/register", new { username, displayName = username, password = Password }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return client;
    }

    private static async Task<string> PlaceOrderAsync(TestApi client, params (string Id, int Quantity)[] items)
    {
        var response = await client.PostAsync("/api/orders", JsonBody(Order(items)));
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode}: {body}");
        return (string)JsonNode.Parse(body)!["id"]!;
    }

    private static async Task<List<string>> OrderIdsAsync(TestApi client) =>
        [.. JsonNode.Parse(await client.GetStringAsync("/api/orders"))!.AsArray().Select(o => (string)o!["id"]!)];

    private static async Task<string> UserIdOfAsync(TestApi client, string orderId) =>
        (string)JsonNode.Parse(await client.GetStringAsync($"/api/orders/{orderId}"))!["userId"]!;

    /// <summary>The status and title of an error response (the detail may well mention the id that was asked for).</summary>
    private static async Task<string> ProblemAsync(HttpResponseMessage response)
    {
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        return $"{(int)response.StatusCode} {problem["title"]}";
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);
}
