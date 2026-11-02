namespace GrotsPizzeria.Shared;

public sealed record UserInfo(bool IsAuthenticated, string? Username = null, string? DisplayName = null);
