namespace GrotsPizzeria.Shared;

/// <summary>A special offer: <see cref="Discount"/> (0.15 is 15%) off, giving <see cref="Price"/>, until <see cref="EndsAt"/>.</summary>
public sealed record MenuOffer(decimal Discount, decimal Price, DateTimeOffset EndsAt)
{
    /// <summary>The discount as a whole percentage: 15 for 15% off.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int PercentOff => (int)Math.Round(Discount * 100m);
}
