namespace GrotsPizzeria.Shared;

/// <summary>What an order may contain. The cart and the BFF's validation both use these, so the UI never builds an order that cannot be placed.</summary>
public static class OrderLimits
{
    /// <summary>The most of one item that can be ordered.</summary>
    public const int MaxQuantity = 20;

    /// <summary>The most different items in one order (more than the menu has, so the cart can never exceed it).</summary>
    public const int MaxLines = 50;
}
