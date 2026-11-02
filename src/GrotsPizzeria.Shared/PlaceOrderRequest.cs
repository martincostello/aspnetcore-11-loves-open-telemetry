using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("An order to place. Who is ordering is whoever is signed in.")]
public sealed record PlaceOrderRequest(
    [property: Description("Where to deliver it: up to 200 characters.")] string Address,
    [property: Description("What is being ordered: from 1 to 50 lines.")] IReadOnlyList<OrderLineRequest> Items,
    PaymentRequest Payment);
