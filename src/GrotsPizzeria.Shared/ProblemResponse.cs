namespace GrotsPizzeria.Shared;

public sealed record ProblemResponse(string Title, int Status, string? Detail = null);
