using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace GrotsPizzeria.Client.Services;

/// <summary>Makes the browser's fetch a "keepalive" one, which is allowed to outlive the page that made it.</summary>
public sealed class KeepAliveHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestOption("keepalive", true);
        return base.SendAsync(request, cancellationToken);
    }
}
