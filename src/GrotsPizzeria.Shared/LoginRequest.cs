using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("The credentials to sign in with.")]
public sealed record LoginRequest(string Username, string Password);
