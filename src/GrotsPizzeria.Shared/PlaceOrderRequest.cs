namespace GrotsPizzeria.Shared;

public sealed record PlaceOrderRequest(string Address, IReadOnlyList<OrderLineRequest> Items, PaymentRequest Payment);
