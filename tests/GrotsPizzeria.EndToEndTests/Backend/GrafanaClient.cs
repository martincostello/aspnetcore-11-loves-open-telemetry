using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace GrotsPizzeria.EndToEndTests.Backend;

/// <summary>
/// The telemetry backend, through Grafana (whose data sources are Tempo for traces, Prometheus for metrics and Loki for logs): what the
/// applications sent is looked at where it ended up, rather than where it was sent from. It is the one of the Docker Compose stack
/// (<c>E2E_GRAFANA_URL</c>, such as http://localhost:3000, with the credentials in <c>E2E_GRAFANA_CREDENTIALS</c>, admin:admin by default).
/// </summary>
public sealed class GrafanaClient : IDisposable
{
    private readonly HttpClient _http;

    private GrafanaClient(Uri address, string credentials)
    {
        _http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
    }

    /// <summary>The backend of the stack that the tests were told about, or <see langword="null"/> if they were not told about one.</summary>
    public static GrafanaClient? FromEnvironment() =>
        Environment.GetEnvironmentVariable("E2E_GRAFANA_URL") is { Length: > 0 } url
            ? new GrafanaClient(new Uri(url), Environment.GetEnvironmentVariable("E2E_GRAFANA_CREDENTIALS") is { Length: > 0 } credentials ? credentials : "admin:admin")
            : null;

    /// <summary>A request to one of the data sources (the uid is "tempo", "prometheus" or "loki"). <see langword="null"/> if it was not found.</summary>
    public async Task<JsonNode?> QueryAsync(string dataSource, string pathAndQuery, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(new Uri($"/api/datasources/proxy/uid/{dataSource}{pathAndQuery}", UriKind.Relative), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return response.IsSuccessStatusCode
            ? JsonNode.Parse(body)
            : throw new HttpRequestException($"{dataSource}{pathAndQuery} returned {(int)response.StatusCode}: {body}");
    }

    /// <summary>The trace, or <see langword="null"/> if the backend does not have it (yet).</summary>
    public async Task<TraceData?> GetTraceAsync(string traceId, CancellationToken cancellationToken)
    {
        var json = await QueryAsync("tempo", $"/api/traces/{traceId}", cancellationToken);

        if (json?["batches"] is not JsonArray batches)
        {
            return null;
        }

        var spans = new List<TraceSpan>();

        foreach (var batch in batches)
        {
            var resource = Attributes(batch?["resource"]?["attributes"]);
            var service = resource.GetValueOrDefault("service.name") ?? "unknown";

            foreach (var scope in batch?["scopeSpans"]?.AsArray() ?? [])
            {
                foreach (var span in scope?["spans"]?.AsArray() ?? [])
                {
                    var parent = (string?)span!["parentSpanId"];

                    spans.Add(new TraceSpan(
                        service,
                        Hex((string)span["traceId"]!),
                        Hex((string)span["spanId"]!),
                        string.IsNullOrEmpty(parent) ? null : Hex(parent),
                        (string)span["name"]!,
                        (string?)span["kind"] ?? "SPAN_KIND_UNSPECIFIED",
                        (string?)span["status"]?["code"] ?? "STATUS_CODE_UNSET",
                        Attributes(span["attributes"])));
                }
            }
        }

        return new TraceData(spans);
    }

    /// <summary>
    /// Waits for the trace to have what the test needs of it: the applications each send their spans when they please (every few
    /// seconds), and the backend takes a moment more. Then, if asked, a little longer, for a test of what is not in it.
    /// </summary>
    public async Task<TraceData> WaitForTraceAsync(string traceId, Func<TraceData, bool> ready, TimeSpan timeout, TimeSpan settle, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        TraceData? trace = null;

        while (true)
        {
            trace = await GetTraceAsync(traceId, cancellationToken) ?? trace;

            if (trace is not null && ready(trace))
            {
                break;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"Trace {traceId} did not have what was needed of it within {timeout.TotalSeconds:0}s. " +
                    (trace is null ? "The backend did not have it at all." : $"This is what it had:{Environment.NewLine}{trace.Describe()}"));
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        if (settle > TimeSpan.Zero)
        {
            await Task.Delay(settle, cancellationToken);
            trace = await GetTraceAsync(traceId, cancellationToken) ?? trace;
        }

        return trace;
    }

    public void Dispose() => _http.Dispose();

    private static string Hex(string base64) => Convert.ToHexStringLower(Convert.FromBase64String(base64));

    private static Dictionary<string, string> Attributes(JsonNode? attributes) =>
        (attributes?.AsArray() ?? [])
            .Where(a => a?["key"] is not null && a["value"] is JsonObject)
            .ToDictionary(
                a => (string)a!["key"]!,
                a => a!["value"]!.AsObject().FirstOrDefault().Value?.ToString() ?? string.Empty,
                StringComparer.Ordinal);
}
