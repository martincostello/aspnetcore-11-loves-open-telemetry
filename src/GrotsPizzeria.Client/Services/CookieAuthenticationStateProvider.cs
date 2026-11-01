using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace GrotsPizzeria.Client.Services;

/// <summary>Asks the BFF who the cookie belongs to (<c>GET /api/auth/me</c>) and tells Blazor about changes.</summary>
public sealed class CookieAuthenticationStateProvider(ApiClient api) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private Task<AuthenticationState>? _state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    public void Refresh()
    {
        _state = LoadAsync();
        NotifyAuthenticationStateChanged(_state);
    }

    private async Task<AuthenticationState> LoadAsync()
    {
        var result = await api.GetCurrentUserAsync();

        if (result.Value is not { IsAuthenticated: true } user)
        {
            return Anonymous;
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user.Username!), new Claim(ClaimTypes.GivenName, user.DisplayName ?? user.Username!)],
            authenticationType: "cookie");

        return new(new ClaimsPrincipal(identity));
    }
}
