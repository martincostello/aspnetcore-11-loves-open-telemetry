using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>
/// Weaver's live-check compares the telemetry that the applications really emit with the OpenTelemetry semantic conventions and with
/// this sample's own (weaver/registry). A run of it, over the traffic that scripts/generate-traffic.ps1 makes, leaves a report; the
/// sample's own signals have to have no violations in it. What the runtimes and the frameworks emit about themselves does not
/// always match the registry, and the known cases are in weaver/allowed-findings.json, each with its reason: anything else fails.
/// <para>The report is the one of a run of <c>build.ps1 -Only telemetry</c>, which is named by <c>E2E_WEAVER_REPORT</c>.</para>
/// </summary>
public sealed class WeaverReportTests
{
    /// <summary>The signals that make this a check of the sample: what a run has to have seen, or it was not a run of the sample.</summary>
    private static readonly string[] SignalsOfTheSample =
    [
        "http.server.request.duration",
        "http.client.request.duration",
        "grot.auth.attempts",
        "grot.checkout.payments",
        "grot.deliveries.active",
        "grot.menu.price_quotes",
        "grot.orders.placed",
    ];

    [Fact]
    public void There_Are_No_Violations_That_Are_Not_Known()
    {
        var report = LoadReport();
        var allowances = LoadAllowances();

        var unexpected = Findings(report)
            .Where(f => f.Level == "violation")
            .Where(f => !allowances.Any(a => a.Allows(f)))
            .GroupBy(f => $"{f.Signal,-8} {f.Name ?? "-",-40} {f.Id}: {f.Message}")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count(),5} x {g.Key}")
            .ToList();

        Assert.True(
            unexpected.Count == 0,
            "These violations of the semantic conventions are not known (fix what emits them, or, if they are about what a runtime or a framework " +
            $"emits about itself, add them to weaver/allowed-findings.json with the reason):{Environment.NewLine}{string.Join(Environment.NewLine, unexpected)}");
    }

    [Fact]
    public void The_Run_Saw_The_Signals_Of_The_Sample_And_The_Registry_Knows_Them_All()
    {
        var statistics = LoadReport()["statistics"]!;

        var known = Names(statistics["seen_registry_metrics"]);
        Assert.All(SignalsOfTheSample, name => Assert.True(known.Contains(name), $"The run did not see {name}: was there any traffic?"));

        // Anything of the sample's that is not in its registry is a signal that someone forgot to define
        Assert.DoesNotContain(Names(statistics["seen_non_registry_metrics"]), n => n.StartsWith("grot.", StringComparison.Ordinal));
        Assert.DoesNotContain(Names(statistics["seen_non_registry_attributes"]), n => n.StartsWith("grot.", StringComparison.Ordinal));
    }

    private static JsonNode LoadReport()
    {
        var path = Environment.GetEnvironmentVariable("E2E_WEAVER_REPORT");
        Assert.SkipUnless(path is { Length: > 0 }, "This looks at the report of a Weaver live-check run: set E2E_WEAVER_REPORT (see build.ps1 -Only telemetry).");

        var file = Path.Combine(RepositoryRoot(), path);
        Assert.True(File.Exists(file), $"The report of the Weaver live-check does not exist: {file}.");

        return JsonNode.Parse(File.ReadAllText(file))!;
    }

    private static List<Allowance> LoadAllowances() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "weaver", "allowed-findings.json")))!["allowances"]!
            .AsArray()
            .Select(a => new Allowance(
                (string)a!["signal"]!,
                (string)a["name"]!,
                (string?)a["finding"],
                (string?)a["messageMatches"],
                (string)a["reason"]!))
            .ToList();

    private static HashSet<string> Names(JsonNode? node) =>
        node switch
        {
            JsonObject o => o.Select(p => p.Key).ToHashSet(),
            JsonArray a => a.Select(n => (string)n!).ToHashSet(),
            _ => [],
        };

    /// <summary>Every finding in the report, with the signal it is about: the metric, span, log or resource, and what that is called.</summary>
    private static IEnumerable<ReportedFinding> Findings(JsonNode report)
    {
        foreach (var sample in report["samples"]!.AsArray())
        {
            var (signal, body) = sample!.AsObject().First();
            var name = (string?)body?["name"];

            foreach (var advice in AllAdvice(body))
            {
                yield return new ReportedFinding(signal, name, (string)advice!["id"]!, (string)advice["level"]!, (string?)advice["message"] ?? string.Empty);
            }
        }
    }

    // The findings are on the signal and on everything inside it: its attributes, the points of a metric, the events of a span
    private static IEnumerable<JsonNode> AllAdvice(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (key, value) in o)
                {
                    if (key == "live_check_result")
                    {
                        foreach (var advice in value?["all_advice"]?.AsArray() ?? [])
                        {
                            yield return advice!;
                        }
                    }
                    else
                    {
                        foreach (var inner in AllAdvice(value))
                        {
                            yield return inner;
                        }
                    }
                }

                break;
            case JsonArray a:
                foreach (var item in a)
                {
                    foreach (var inner in AllAdvice(item))
                    {
                        yield return inner;
                    }
                }

                break;
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

    private sealed record ReportedFinding(string Signal, string? Name, string Id, string Level, string Message);

    private sealed record Allowance(string Signal, string Name, string? Finding, string? MessageMatches, string Reason)
    {
        public bool Allows(ReportedFinding finding) =>
            finding.Signal == Signal &&
            Glob(Name).IsMatch(finding.Name ?? string.Empty) &&
            (Finding is null || finding.Id == Finding) &&
            (MessageMatches is null || Regex.IsMatch(finding.Message, MessageMatches));

        private static Regex Glob(string pattern) =>
            new("^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$", RegexOptions.None, TimeSpan.FromSeconds(1));
    }
}
