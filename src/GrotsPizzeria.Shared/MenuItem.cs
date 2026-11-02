using System.ComponentModel;

namespace GrotsPizzeria.Shared;

/// <summary><see cref="Price"/> is the list price; when the item is on special offer the <see cref="Offer"/> has what it costs now.</summary>
public sealed record MenuItem(
    string Id,
    string Name,
    string Description,
    string Category,
    decimal Price,
    [property: Description("The ISO 4217 code of the currency of the prices of the item, and of its offer.")] string Currency,
    bool Vegetarian,
    bool Popular,
    string Emoji,
    MenuOffer? Offer = null)
{
    /// <summary>What one costs right now.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal CurrentPrice => Offer?.Price ?? Price;
}
