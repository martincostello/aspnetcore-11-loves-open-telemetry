using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("One line of an order, priced.")]
public sealed record OrderLine(
    [property: Description("The id of the menu item.")] string PizzaId,
    string Name,
    int Quantity,
    [property: Description("What one cost when the order was placed (the offer price, if it was on offer), in the order's currency.")] decimal UnitPrice,
    decimal LineTotal);
