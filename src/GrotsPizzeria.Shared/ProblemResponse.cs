using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("What every error response looks like, whichever service it came from.")]
public sealed record ProblemResponse(
    [property: Description("A short summary of what went wrong.")] string Title,
    [property: Description("The HTTP status code.")] int Status,
    [property: Description("What went wrong, in full.")] string? Detail = null);
