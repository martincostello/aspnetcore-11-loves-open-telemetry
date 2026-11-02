using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("An order that was placed.")]
public sealed record Order(
    [property: Description("The id of the order.")] string Id,
    [property: Description("Who placed it.")] string UserId,
    string UserName,
    [property: Description("Where the order is up to: `confirmed` once it has been paid for.")] string Status,
    [property: Description("Where it is being delivered to.")] string Address,
    IReadOnlyList<OrderLine> Items,
    [property: Description("The price of the items, before tax, in pounds.")] decimal Subtotal,
    [property: Description("The tax charged, in pounds.")] decimal Tax,
    [property: Description("The rate the tax was charged at, as a fraction: 0.08 is 8%.")] decimal TaxRate,
    [property: Description("What was charged: the subtotal plus the tax, in pounds.")] decimal Total,
    string Currency,
    [property: Description("The transaction of the payment.")] string TransactionId,
    DateTimeOffset CreatedAt);
