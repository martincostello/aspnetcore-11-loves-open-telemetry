using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Client.Services;

public sealed record CartLine(MenuItem Item, int Quantity)
{
    public decimal Total => Item.CurrentPrice * Quantity;
}
