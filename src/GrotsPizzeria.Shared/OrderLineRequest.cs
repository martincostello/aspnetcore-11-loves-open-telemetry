namespace GrotsPizzeria.Shared;

public sealed record OrderLineRequest(string PizzaId, int Quantity);
