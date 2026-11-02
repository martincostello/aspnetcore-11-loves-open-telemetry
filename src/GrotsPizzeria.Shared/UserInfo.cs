using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("Who is signed in, if anyone.")]
public sealed record UserInfo(
    [property: Description("Whether the request has a signed-in user.")] bool IsAuthenticated,
    string? Username = null,
    string? DisplayName = null);
