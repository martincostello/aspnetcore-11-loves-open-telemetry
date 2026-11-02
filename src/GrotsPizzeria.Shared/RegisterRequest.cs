using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("A new account.")]
public sealed record RegisterRequest(
    [property: Description("What to sign in with.")] string Username,
    [property: Description("What to call them, at most 50 characters (the username if it is left out).")] string DisplayName,
    [property: Description("At least 8 characters.")] string Password);
