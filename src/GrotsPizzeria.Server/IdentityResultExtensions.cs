using Microsoft.AspNetCore.Identity;

namespace GrotsPizzeria.Server;

internal static class IdentityResultExtensions
{
    public static void ThrowOnFailure(this IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(' ', result.Errors.Select(e => e.Description)));
        }
    }
}
