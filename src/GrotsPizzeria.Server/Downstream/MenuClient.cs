using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Server.Downstream;

/// <summary>grots-menu (Node.js).</summary>
public sealed class MenuClient(HttpClient client)
{
    public Task<DownstreamResult<IReadOnlyList<MenuItem>>> GetMenuAsync(CancellationToken cancellationToken) =>
        DownstreamHttp.SendAsync<IReadOnlyList<MenuItem>>(client, new(HttpMethod.Get, "menu"), "Menu service", cancellationToken);
}
