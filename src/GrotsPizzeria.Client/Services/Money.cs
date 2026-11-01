using System.Globalization;

namespace GrotsPizzeria.Client.Services;

public static class Money
{
    /// <summary>Formats an amount as pounds sterling. (Explicit, because the app runs with invariant globalization.)</summary>
    public static string Gbp(this decimal amount) => $"£{amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
