using Microsoft.AspNetCore.Identity;

namespace GrotsPizzeria.Server.Auth;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}
