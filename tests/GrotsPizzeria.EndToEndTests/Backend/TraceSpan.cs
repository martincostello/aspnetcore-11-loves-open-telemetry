namespace GrotsPizzeria.EndToEndTests.Backend;

/// <summary>One span of a trace, as the backend (Tempo) has stored it.</summary>
public sealed record TraceSpan(
    string Service,
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    string Kind,
    string StatusCode,
    IReadOnlyDictionary<string, string> Attributes)
{
    public bool IsServer => Kind == "SPAN_KIND_SERVER";

    public bool IsClient => Kind == "SPAN_KIND_CLIENT";

    /// <summary>Whether the span says that it failed (the status is Error: Unset is what a span that has not failed says).</summary>
    public bool IsError => StatusCode == "STATUS_CODE_ERROR";

    public string? Attribute(string key) => Attributes.GetValueOrDefault(key);

    public override string ToString() =>
        $"{Service} {Kind.Replace("SPAN_KIND_", string.Empty, StringComparison.Ordinal)} '{Name}' status={StatusCode.Replace("STATUS_CODE_", string.Empty, StringComparison.Ordinal)} " +
        $"http={Attribute("http.request.method")} {Attribute("http.route") ?? Attribute("url.full")} -> {Attribute("http.response.status_code")}";
}
