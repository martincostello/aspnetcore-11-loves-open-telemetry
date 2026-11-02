using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Client.Services;

public sealed record ApiResult<T>(T? Value, ProblemResponse? Problem)
{
    public bool IsSuccess => Problem is null;
}
