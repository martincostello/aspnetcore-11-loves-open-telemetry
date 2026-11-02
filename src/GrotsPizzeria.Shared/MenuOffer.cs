using System.Globalization;
using System.Text.Json.Serialization;

namespace GrotsPizzeria.Shared;

/// <summary>A special offer: <see cref="Discount"/> (0.15 is 15%) off, giving <see cref="Price"/>, until <see cref="EndsAt"/>.</summary>
public sealed record MenuOffer(decimal Discount, decimal Price, DateTimeOffset EndsAt)
{
    /// <summary>The discount as a percentage: 15 for 15% off, and 12.5 for 12.5% off (it is not rounded to a whole number).</summary>
    [JsonIgnore]
    public decimal PercentOff => Math.Round(Discount * 100m, 2);

    /// <summary><see cref="PercentOff"/> as text, with only the decimal places it needs: "15" and "12.5".</summary>
    [JsonIgnore]
    public string PercentOffText => PercentOff.ToString("0.##", CultureInfo.InvariantCulture);
}
