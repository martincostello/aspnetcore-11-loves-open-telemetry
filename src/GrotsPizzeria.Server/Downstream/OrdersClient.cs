using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Server.Downstream;

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
