using System.Globalization;
using System.Text.Json.Nodes;
using GrotsPizzeria.EndToEndTests.Backend;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>
/// The queries of the Grafana dashboard (deploy/grafana/dashboards) are run against the backend that the applications sent their
/// telemetry to: that they are valid JSON, and that the panels are not empty in a file, does not mean that they show anything. Each
/// of them has to answer, with something, once there has been some traffic (scripts/generate-traffic.ps1), apart from the few that
/// are about things going wrong in a way that traffic does not make happen.
/// <para>These need the telemetry of the Docker Compose stack: <c>E2E_GRAFANA_URL</c>, such as http://localhost:3000.</para>
/// </summary>
public sealed class DashboardQueryTests
{
    /// <summary>How long to wait for the backend to have an answer for a panel before saying that it has none.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>Panels that are allowed to show nothing, and why.</summary>
    private static readonly Dictionary<string, string> MayBeEmpty = new()
    {
        ["Failed queries / s"] = "Database queries fail only when something is wrong with the database, which the traffic does not do.",
        ["Slow or failing queries"] = "Database queries are only slow or failing when something is wrong with the database.",
    };

    public static TheoryData<string, string, string, string, bool> Queries()
    {
        var data = new TheoryData<string, string, string, string, bool>();
        var dashboard = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy", "grafana", "dashboards", "grots-http-semconv.json")))!;

        foreach (var panel in Panels(dashboard["panels"]!.AsArray()))
        {
            var title = (string)panel["title"]!;

            foreach (var target in panel["targets"]?.AsArray() ?? [])
            {
                var dataSource = (string)(target!["datasource"] ?? panel["datasource"])!["uid"]!;
                var query = (string?)target["expr"] ?? (string)target["query"]!;
                var kind = dataSource switch
                {
                    "prometheus" => target["instant"]?.GetValue<bool>() is true ? "instant" : "range",
                    "loki" => "logs",
                    "tempo" => target["metricsQueryType"] is null ? "spans" : "span-metrics",
                    _ => throw new NotSupportedException($"Panel '{title}' queries '{dataSource}', which this test does not know how to ask."),
                };

                data.Add(title, dataSource, kind, query, MayBeEmpty.ContainsKey(title));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Every_Query_Of_The_Dashboard_Has_An_Answer(string panel, string dataSource, string kind, string query, bool mayBeEmpty)
    {
        using var grafana = GrafanaClient.FromEnvironment();
        Assert.SkipUnless(grafana is not null, "This asks the backend of a running Docker Compose stack: set E2E_GRAFANA_URL (see the README).");

        var text = Uri.EscapeDataString(Substitute(query));
        var seconds = (DateTimeOffset t) => t.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        // The backend takes a while to make what it was sent available to queries, and for different amounts of time for logs,
        // for traces and for the metrics that are made from them (more so on a busy machine, such as a CI runner that has just
        // started everything), so a panel is asked until it has an answer, and only has nothing to show if it never does.
        // Panels that may be empty are asked once.
        var deadline = DateTime.UtcNow + (mayBeEmpty ? TimeSpan.Zero : Patience);
        var count = 0;

        while (true)
        {
            var end = DateTimeOffset.UtcNow;
            var start = end.AddMinutes(-30);

            // The question that the panel asks, and where in the answer that it is a list of what was found
            var (path, answer) = kind switch
            {
                "instant" => ($"/api/v1/query?query={text}&time={seconds(end)}", "data.result"),
                "range" => ($"/api/v1/query_range?query={text}&start={seconds(start)}&end={seconds(end)}&step=60", "data.result"),
                "logs" => ($"/loki/api/v1/query_range?query={text}&limit=5&start={seconds(start)}000000000&end={seconds(end)}000000000", "data.result"),
                "spans" => ($"/api/search?q={text}&limit=5&start={seconds(start)}&end={seconds(end)}", "traces"),
                "span-metrics" => ($"/api/metrics/query_range?q={text}&start={seconds(start)}&end={seconds(end)}&step=60s", "series"),
                _ => throw new NotSupportedException(kind),
            };

            var json = await grafana!.QueryAsync(dataSource, path, TestContext.Current.CancellationToken);
            Assert.NotNull(json);

            JsonNode? found = json;

            foreach (var name in answer.Split('.'))
            {
                found = found?[name];
            }

            count = found?.AsArray().Count ?? 0;

            if (count > 0 || DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }

        Assert.True(
            count > 0 || mayBeEmpty,
            $"The panel '{panel}' has nothing to show: {kind} {dataSource} query '{query}' found nothing in the last 30 minutes, in {Patience.TotalSeconds:N0} seconds of asking. " +
            "Has there been any traffic (scripts/generate-traffic.ps1)? If so, the query is wrong.");
    }

    /// <summary>What Grafana puts in a query for the dashboard's variables and the time range, in terms of everything.</summary>
    private static string Substitute(string query) =>
        query
            .Replace("${service:regex}", ".+", StringComparison.Ordinal)
            .Replace("${service}", ".+", StringComparison.Ordinal)
            .Replace("$service", ".+", StringComparison.Ordinal)
            .Replace("$__rate_interval", "5m", StringComparison.Ordinal)
            .Replace("$__range", "30m", StringComparison.Ordinal)
            .Replace("$search", string.Empty, StringComparison.Ordinal);

    /// <summary>The panels that ask something: not the rows and the notes, and including what is inside a row that is folded away.</summary>
    private static IEnumerable<JsonNode> Panels(JsonArray panels)
    {
        foreach (var panel in panels)
        {
            if (panel!["panels"] is JsonArray nested)
            {
                foreach (var inner in Panels(nested))
                {
                    yield return inner;
                }
            }

            if ((string)panel["type"]! is not ("row" or "text"))
            {
                yield return panel;
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GrotsPizzeria.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository was not found above the tests.");
    }
}
