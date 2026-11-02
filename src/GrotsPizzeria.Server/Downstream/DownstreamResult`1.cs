using System.Net;
using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Server.Downstream;

/// <summary>The outcome of a call to one of the polyglot microservices: either a value or the problem it reported.</summary>
public sealed record DownstreamResult<T>(T? Value, ProblemResponse? Problem, HttpStatusCode StatusCode)
{
    public bool IsSuccess => Problem is null;

    public IResult ToHttpResult() =>
        Problem is null
            ? Results.Json(Value)
            : Results.Json(Problem, statusCode: (int)StatusCode);
}
