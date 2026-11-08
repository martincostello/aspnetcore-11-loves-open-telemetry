using System.Text.Json.Serialization;

namespace GrotsPizzeria.Shared;

/// <summary>What the UI's "See telemetry" link goes to, which is what the UI shows the logo of.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TelemetryProvider>))]
public enum TelemetryProvider
{
    /// <summary>The provisioned dashboard in Grafana (the Docker Compose stack).</summary>
    Grafana,

    /// <summary>The Aspire dashboard (the AppHost).</summary>
    Aspire,
}
