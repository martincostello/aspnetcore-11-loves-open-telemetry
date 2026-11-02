using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("How an order is paid for.")]
public sealed record PaymentRequest(
    [property: Description("How the customer is paying: `card` or `cash`.")] string Method,
    [property: Description("The last 4 digits of the card: required for card payments. `0000` is always declined.")] string? CardLast4);
