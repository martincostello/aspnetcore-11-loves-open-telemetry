namespace GrotsPizzeria.EndToEndTests.Backend;

/// <summary>A trace as the backend has it: the spans that every application sent for it.</summary>
public sealed class TraceData(IReadOnlyList<TraceSpan> spans)
{
    public IReadOnlyList<TraceSpan> Spans { get; } = spans;

    public IReadOnlySet<string> Services => Spans.Select(s => s.Service).ToHashSet();

    public TraceSpan? ParentOf(TraceSpan span) =>
        span.ParentSpanId is null ? null : Spans.FirstOrDefault(s => s.SpanId == span.ParentSpanId);

    /// <summary>The parent of the span, and its parent, and so on up to where the trace starts.</summary>
    public IReadOnlyList<TraceSpan> Ancestors(TraceSpan span)
    {
        var ancestors = new List<TraceSpan>();

        for (var parent = ParentOf(span); parent is not null; parent = ParentOf(parent))
        {
            ancestors.Add(parent);
        }

        return ancestors;
    }

    /// <summary>The spans with no parent in the trace: where it starts.</summary>
    public IEnumerable<TraceSpan> Roots => Spans.Where(s => ParentOf(s) is null);

    /// <summary>
    /// Whether everything that was sent for the trace has arrived, as far as can be told: each of the applications has its server span for its route,
    /// and every span has its parent (so that there is one place where the trace starts, and not spans waiting for a parent that has not arrived).
    /// </summary>
    public bool IsComplete(IEnumerable<(string Service, string Route)> servers) =>
        Roots.Count() == 1 && servers.All(s => Spans.Any(x => x.IsServer && x.Service == s.Service && x.Attribute("http.route") == s.Route));

    /// <summary>The one server span of the application for the route (the template, such as <c>/orders/{id}</c>).</summary>
    public TraceSpan Server(string service, string route) =>
        Single($"server span of {service} for {route}", s => s.IsServer && s.Service == service && s.Attribute("http.route") == route);

    /// <summary>The one client span of the application for a request whose URL ends with the text.</summary>
    public TraceSpan Client(string service, string urlEndsWith) =>
        Single($"client span of {service} for a URL ending {urlEndsWith}", s => s.IsClient && s.Service == service && s.Attribute("url.full")?.EndsWith(urlEndsWith, StringComparison.Ordinal) is true);

    public string Describe() => string.Join(Environment.NewLine, Spans.OrderBy(s => s.Service).ThenBy(s => s.Name).Select(s => "  " + s));

    private TraceSpan Single(string what, Func<TraceSpan, bool> predicate)
    {
        var matches = Spans.Where(predicate).ToList();

        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException($"Expected one {what}, but found {matches.Count}. The trace:{Environment.NewLine}{Describe()}");
    }
}
