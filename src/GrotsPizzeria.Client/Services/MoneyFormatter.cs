using System.Globalization;
using Microsoft.JSInterop;

namespace GrotsPizzeria.Client.Services;

/// <summary>
/// Formats an amount of money in the currency the API says it is in (an ISO 4217 code), the way the browser's locale writes
/// it: "£9.50", "9,50 €" and so on. (The app runs with invariant globalization, which has no currencies of its own, so
/// the browser's Intl does it, in wwwroot/js/format.js.)
/// </summary>
public sealed class MoneyFormatter(IJSRuntime js)
{
    public string Format(decimal amount, string currency)
    {
        try
        {
            if (js is IJSInProcessRuntime inProcess)
            {
                return inProcess.Invoke<string>("grotsFormat.currency", amount, currency);
            }
        }
        catch (JSException)
        {
            // Fall through: the amount is still shown, just without the locale's way of writing it.
        }

        return $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";
    }
}
