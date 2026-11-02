namespace GrotsPizzeria.Shared;

public sealed record OrderLine(string PizzaId, string Name, int Quantity, decimal UnitPrice, decimal LineTotal);
