using Microsoft.JSInterop;

namespace GrotsPizzeria.Client.Services;

/// <summary>
/// The user's theme preference: "system" (the default, follows the OS), "light" or "dark".
/// The work happens in wwwroot/js/theme.js so the theme can be applied before Blazor starts (no flash).
/// </summary>
public sealed class ThemeService(IJSRuntime js)
{
    public async ValueTask<string> GetAsync()
    {
        try
        {
            return await js.InvokeAsync<string>("grotsTheme.get");
        }
        catch (JSException)
        {
            return "system";
        }
    }

    public async ValueTask SetAsync(string preference)
    {
        try
        {
            await js.InvokeVoidAsync("grotsTheme.set", preference);
        }
        catch (JSException)
        {
            // The theme is cosmetic; never break the page over it.
        }
    }
}
