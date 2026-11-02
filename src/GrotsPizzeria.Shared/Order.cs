namespace GrotsPizzeria.Shared;

public sealed record Order(
    string Id,
    string UserId,
    string UserName,
    string Status,
    string Address,
    IReadOnlyList<OrderLine> Items,
    decimal Subtotal,
    decimal Tax,
    decimal TaxRate,
    decimal Total,
    string Currency,
    string TransactionId,
    DateTimeOffset CreatedAt);
