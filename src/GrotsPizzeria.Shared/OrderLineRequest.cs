using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("Some of one menu item.")]
public sealed record OrderLineRequest(
    [property: Description("The id of any menu item: a pizza, a side or a dessert.")] string PizzaId,
    [property: Description("How many, from 1 to 20.")] int Quantity);
