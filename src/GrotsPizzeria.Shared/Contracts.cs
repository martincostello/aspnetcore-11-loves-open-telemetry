namespace GrotsPizzeria.Shared;

// Wire contracts shared by the Blazor client and the ASP.NET Core BFF.
// They mirror docs/service-contract.md; the BFF forwards the downstream JSON through untouched.

/// <summary>A special offer: <see cref="Discount"/> (0.15 is 15%) off, giving <see cref="Price"/>, until <see cref="EndsAt"/>.</summary>
public sealed record MenuOffer(decimal Discount, decimal Price, DateTimeOffset EndsAt)
{
    /// <summary>The discount as a whole percentage: 15 for 15% off.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int PercentOff => (int)Math.Round(Discount * 100m);
}

/// <summary><see cref="Price"/> is the list price; when the item is on special offer the <see cref="Offer"/> has what it costs now.</summary>
public sealed record MenuItem(string Id, string Name, string Description, string Category, decimal Price, bool Vegetarian, bool Popular, string Emoji, MenuOffer? Offer = null)
{
    /// <summary>What one costs right now.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal CurrentPrice => Offer?.Price ?? Price;
}

/// <summary>What an order may contain. The cart and the BFF's validation both use these, so the UI never builds an order that cannot be placed.</summary>
public static class OrderLimits
{
    /// <summary>The most of one item that can be ordered.</summary>
    public const int MaxQuantity = 20;

    /// <summary>The most different items in one order (more than the menu has, so the cart can never exceed it).</summary>
    public const int MaxLines = 50;
}

public sealed record OrderLineRequest(string PizzaId, int Quantity);

public sealed record PaymentRequest(string Method, string? CardLast4);

public sealed record PlaceOrderRequest(string Address, IReadOnlyList<OrderLineRequest> Items, PaymentRequest Payment);

public sealed record OrderLine(string PizzaId, string Name, int Quantity, decimal UnitPrice, decimal LineTotal);

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

public sealed record TrackingStep(string Status, string Label, bool Completed, bool Reached, DateTimeOffset At);

public sealed record Tracking(
    string OrderId,
    string Address,
    string Status,
    string Driver,
    int EtaMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TrackingStep> Steps);

public sealed record RegisterRequest(string Username, string DisplayName, string Password);

public sealed record LoginRequest(string Username, string Password);

public sealed record UserInfo(bool IsAuthenticated, string? Username = null, string? DisplayName = null);

public sealed record ProblemResponse(string Title, int Status, string? Detail = null);

/// <summary>Settings the BFF hands to the UI at startup.</summary>
public sealed record ClientConfig(string TelemetryUrl, string? OtlpEndpoint = null);
