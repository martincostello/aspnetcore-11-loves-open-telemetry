using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("How the delivery of an order is going.")]
public sealed record Tracking(
    string OrderId,
    string Address,
    [property: Description("The step the delivery is at now: `preparing`, `baking`, `quality-control`, `out-for-delivery` or `delivered`.")] string Status,
    [property: Description("Who is delivering it.")] string Driver,
    [property: Description("How long until it arrives, in minutes (0 once it has).")] int EtaMinutes,
    DateTimeOffset CreatedAt,
    [property: Description("When the status last changed.")] DateTimeOffset UpdatedAt,
    [property: Description("Every step of the delivery, in order, and how far it has got.")] IReadOnlyList<TrackingStep> Steps);
