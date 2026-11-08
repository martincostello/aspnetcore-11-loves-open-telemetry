namespace GrotsPizzeria.EndToEndTests.Backend;

/// <summary>A trace context that a test chooses, so that the trace can be found afterwards by its id.</summary>
public sealed record TestTrace(string TraceId, string SpanId)
{
    public string TraceParent => $"00-{TraceId}-{SpanId}-01";

    public static TestTrace New() => new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")[..16]);
}
