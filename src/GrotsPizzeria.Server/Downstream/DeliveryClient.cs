using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Server.Downstream;

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
