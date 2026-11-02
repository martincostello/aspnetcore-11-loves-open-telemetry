using System.Diagnostics.Metrics;

namespace GrotsPizzeria.Server;

/// <summary>
/// The application's own telemetry. Everything about HTTP (server, client, Kestrel, runtime) comes from the
/// instrumentation that is built in to ASP.NET Core and .NET; this is only the "grot.*" business signals.
/// </summary>
public sealed class Telemetry
{
    public const string Name = "Grots.Web";

    private readonly Counter<long> _authAttempts;

    public Telemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _authAttempts = meter.CreateCounter<long>(
            "grot.auth.attempts",
            unit: "{attempt}",
            description: "The number of attempts to log in or register.");
    }

    public void RecordAuth(string operation, bool success) =>
        _authAttempts.Add(
            1,
            new KeyValuePair<string, object?>("grot.auth.operation", operation),
            new KeyValuePair<string, object?>("grot.auth.outcome", success ? "success" : "failure"));
}
