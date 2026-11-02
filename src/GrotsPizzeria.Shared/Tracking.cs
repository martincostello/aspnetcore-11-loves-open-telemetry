namespace GrotsPizzeria.Shared;

public sealed record Tracking(
    string OrderId,
    string Address,
    string Status,
    string Driver,
    int EtaMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TrackingStep> Steps);
