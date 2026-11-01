using System.Diagnostics;
using OpenTelemetry.Trace;

namespace GrotsPizzeria.Server;

/// <summary>
/// Drops the server span of health check requests (GET /healthz), which orchestrators make every few seconds, so they do not
/// bury the traces that matter. ASP.NET Core's tracing is built in, so there is no instrumentation option for this: the
/// request's URL path is available to the sampler when the span is about to be created, and a sampler is the place to refuse it.
/// </summary>
internal sealed class HealthCheckSampler(Sampler inner, string path = "/healthz") : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (samplingParameters.Kind == ActivityKind.Server && samplingParameters.Tags is { } tags)
        {
            foreach (var (key, value) in tags)
            {
                if (key == "url.path" && value is string requested && string.Equals(requested, path, StringComparison.Ordinal))
                {
                    return new SamplingResult(SamplingDecision.Drop);
                }
            }
        }

        return inner.ShouldSample(samplingParameters);
    }
}
