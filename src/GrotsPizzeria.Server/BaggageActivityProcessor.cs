using System.Diagnostics;
using OpenTelemetry;

namespace GrotsPizzeria.Server;

/// <summary>
/// Copies the sample's own baggage (keys starting "grot.") onto every span as an attribute, so that the context the browser
/// sent (for example grot.ui.action) can be seen on each span in the trace. Baggage is not recorded on telemetry by default.
/// </summary>
internal sealed class BaggageActivityProcessor : BaseProcessor<Activity>
{
    public override void OnStart(Activity data)
    {
        foreach (var (key, value) in data.Baggage)
        {
            if (key.StartsWith("grot.", StringComparison.Ordinal) && data.GetTagItem(key) is null)
            {
                data.SetTag(key, value);
            }
        }
    }
}
