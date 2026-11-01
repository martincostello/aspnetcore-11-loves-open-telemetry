using OpenTelemetry.Trace;

namespace GrotsPizzeria.Server;

/// <summary>
/// The sampler chosen by the standard <c>OTEL_TRACES_SAMPLER</c> and <c>OTEL_TRACES_SAMPLER_ARG</c> settings.
/// The SDK does this itself only when no sampler is set in code, and the health check filter has to wrap whichever one is
/// wanted (see <see cref="HealthCheckSampler"/>), so it is done here, for the values the specification defines.
/// </summary>
internal static class ConfiguredSampler
{
    public static Sampler Create(IConfiguration configuration)
    {
        var name = configuration["OTEL_TRACES_SAMPLER"]?.Trim().ToLowerInvariant();
        var argument = configuration["OTEL_TRACES_SAMPLER_ARG"];

        return name switch
        {
            "always_on" => new AlwaysOnSampler(),
            "always_off" => new AlwaysOffSampler(),
            "traceidratio" => new TraceIdRatioBasedSampler(Ratio(argument)),
            "parentbased_always_off" => new ParentBasedSampler(new AlwaysOffSampler()),
            "parentbased_traceidratio" => new ParentBasedSampler(new TraceIdRatioBasedSampler(Ratio(argument))),
            _ => new ParentBasedSampler(new AlwaysOnSampler()),
        };
    }

    private static double Ratio(string? argument) =>
        double.TryParse(argument, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ratio) && ratio is >= 0 and <= 1
            ? ratio
            : 1;
}
