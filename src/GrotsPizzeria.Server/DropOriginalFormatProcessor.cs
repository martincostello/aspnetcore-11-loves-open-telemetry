using OpenTelemetry;
using OpenTelemetry.Logs;

namespace GrotsPizzeria.Server;

/// <summary>
/// The .NET logging bridge exports the message template as a "{OriginalFormat}" attribute next to the formatted body.
/// That is not a semantic convention attribute, and the template is not needed to read the log, so it is removed.
/// </summary>
internal sealed class DropOriginalFormatProcessor : BaseProcessor<LogRecord>
{
    private const string OriginalFormat = "{OriginalFormat}";

    public override void OnEnd(LogRecord data)
    {
        if (data.Attributes is { } attributes && attributes.Any(a => a.Key == OriginalFormat))
        {
            data.Attributes = [.. attributes.Where(a => a.Key != OriginalFormat)];
        }
    }
}
