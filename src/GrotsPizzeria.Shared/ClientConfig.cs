namespace GrotsPizzeria.Shared;

/// <summary>Settings the BFF hands to the UI at startup.</summary>
public sealed record ClientConfig(
    string TelemetryUrl,
    string? OtlpEndpoint = null,
    TelemetryProvider TelemetryProvider = TelemetryProvider.Grafana);
