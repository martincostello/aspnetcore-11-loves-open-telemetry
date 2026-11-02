using System.Net;
using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Server.Downstream;

internal static class DownstreamHttp
{
    /// <summary>
    /// Sends the request and maps non-success responses (and transport failures) to a <see cref="ProblemResponse"/>
    /// so the UI always gets the same error shape regardless of which language produced it.
    /// </summary>
    public static async Task<DownstreamResult<T>> SendAsync<T>(
        HttpClient client,
        HttpRequestMessage request,
        string service,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
                return new(value, null, response.StatusCode);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken)
                ?? new ProblemResponse($"{service} returned an error", (int)response.StatusCode);

            return new(default, problem, response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new(
                default,
                new ProblemResponse($"{service} is unavailable", StatusCodes.Status502BadGateway, ex.Message),
                HttpStatusCode.BadGateway);
        }
    }

    private static async Task<ProblemResponse?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
