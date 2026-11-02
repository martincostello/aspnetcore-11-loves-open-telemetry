using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GrotsPizzeria.Server.Auth;

/// <summary>
/// Mints the short-lived JWTs that secure calls from the BFF to the other services (see docs/service-contract.md).
/// The Go, Java, Node.js and Python services all validate them with the same shared HS256 key.
/// </summary>
public sealed class ServiceTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeProvider _time;

    public ServiceTokenIssuer(IConfiguration configuration, TimeProvider time)
    {
        var key = configuration["JWT_SIGNING_KEY"];

        if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("JWT_SIGNING_KEY must be set to at least 32 bytes.");
        }

        _credentials = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        _issuer = configuration["JWT_ISSUER"] ?? "grots-web";
        _audience = configuration["JWT_AUDIENCE"] ?? "grots-pizzeria";
        _time = time;
    }

    public string CreateToken(ClaimsPrincipal? user)
    {
        var authenticated = user?.Identity?.IsAuthenticated is true;
        var now = _time.GetUtcNow().UtcDateTime;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = authenticated ? user!.FindFirstValue(ClaimTypes.NameIdentifier)! : "anonymous",
        };

        if (authenticated)
        {
            claims["name"] = user!.Identity!.Name!;
        }

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = claims,
            SigningCredentials = _credentials,
        });
    }
}
