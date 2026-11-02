namespace GrotsPizzeria.Server.Auth;

/// <summary>Attaches a freshly minted JWT, on behalf of the current user, to every outbound service call.</summary>
public sealed class ServiceTokenHandler(ServiceTokenIssuer issuer, IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new("Bearer", issuer.CreateToken(accessor.HttpContext?.User));
        return base.SendAsync(request, cancellationToken);
    }
}
