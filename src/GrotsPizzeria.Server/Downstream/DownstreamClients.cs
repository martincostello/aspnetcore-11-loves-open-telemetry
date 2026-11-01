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

/// <summary>grots-menu (Node.js).</summary>
public sealed class MenuClient(HttpClient client)
{
    public Task<DownstreamResult<IReadOnlyList<MenuItem>>> GetMenuAsync(CancellationToken cancellationToken) =>
        DownstreamHttp.SendAsync<IReadOnlyList<MenuItem>>(client, new(HttpMethod.Get, "menu"), "Menu service", cancellationToken);
}

/// <summary>grots-orders (Java).</summary>
public sealed class OrdersClient(HttpClient client)
{
    public Task<DownstreamResult<Order>> PlaceOrderAsync(
        string userId,
        string userName,
        PlaceOrderRequest order,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "orders")
        {
            Content = JsonContent.Create(new
            {
                userId,
                userName,
                order.Address,
                order.Items,
                order.Payment,
            }),
        };

        return DownstreamHttp.SendAsync<Order>(client, request, "Orders service", cancellationToken);
    }

    public Task<DownstreamResult<IReadOnlyList<Order>>> GetOrdersAsync(string userId, CancellationToken cancellationToken) =>
        DownstreamHttp.SendAsync<IReadOnlyList<Order>>(
            client,
            new(HttpMethod.Get, $"orders?userId={Uri.EscapeDataString(userId)}"),
            "Orders service",
            cancellationToken);

    public Task<DownstreamResult<Order>> GetOrderAsync(string id, CancellationToken cancellationToken) =>
        DownstreamHttp.SendAsync<Order>(
            client,
            new(HttpMethod.Get, $"orders/{Uri.EscapeDataString(id)}"),
            "Orders service",
            cancellationToken);
}

/// <summary>grots-delivery (Go).</summary>
public sealed class DeliveryClient(HttpClient client)
{
    public Task<DownstreamResult<Tracking>> GetTrackingAsync(string orderId, CancellationToken cancellationToken) =>
        DownstreamHttp.SendAsync<Tracking>(
            client,
            new(HttpMethod.Get, $"deliveries/{Uri.EscapeDataString(orderId)}"),
            "Delivery service",
            cancellationToken);
}
