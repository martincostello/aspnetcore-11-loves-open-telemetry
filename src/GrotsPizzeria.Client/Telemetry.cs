using System.Diagnostics;
using GrotsPizzeria.Client.Services;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GrotsPizzeria.Client;

/// <summary>
/// Telemetry for the part of the application that runs in the browser, using the OpenTelemetry .NET SDK.
/// <para>
/// A browser has no environment variables, so the BFF tells it where to send OTLP (BROWSER_OTLP_ENDPOINT, via
/// GET /api/config). That address is the one the browser can reach, which is not necessarily the one the containers use,
/// and the collector has to allow cross-origin requests from the page (LGTM's does for http:// origins).
/// </para>
/// </summary>
public static class Telemetry
{
    public const string Name = "Grots.Ui";

    /// <summary>The spans for what the user does in the UI, such as placing an order.</summary>
    public static readonly ActivitySource Source = new(Name);

    public static WebAssemblyHostBuilder AddTelemetry(this WebAssemblyHostBuilder builder, Uri otlpEndpoint)
    {
        var otlp = new Uri(otlpEndpoint.AbsoluteUri.TrimEnd('/') + "/");

        // The SDK is configured in code: there are no OTEL_* environment variables in a browser.
        void Configure(OtlpExporterOptions options, string signal)
        {
            options.Endpoint = new Uri(otlp, $"v1/{signal}");
            options.Protocol = OtlpExportProtocol.HttpProtobuf;

            // keepalive lets a request that is made as the page goes away (see FlushTelemetry) complete anyway
            options.HttpClientFactory = () => new HttpClient(new KeepAliveHandler { InnerHandler = new HttpClientHandler() });
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService("grots-ui", serviceNamespace: "grots-pizzeria")
                .AddAttributes([new("telemetry.distro.name", "blazor-webassembly")]))
            .WithTracing(tracing =>
            {
                // Record every trace while developing (see the AppHost); the default sampler honours upstream decisions.
                if (builder.HostEnvironment.IsDevelopment())
                {
                    tracing.SetSampler(new AlwaysOnSampler());
                }

                // What Blazor does with components (built in), plus our own spans: UI actions and HTTP client calls (TracingHandler)
                tracing
                    .AddSource("Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Lifecycle", Name)
                    .AddOtlpExporter(options =>
                    {
                        Configure(options, "traces");

                        // Send spans quickly: the browser's spans are the parents of the server's, and until they arrive the
                        // backend shows the server's spans in a trace with no root, which they never get if the page is closed first.
                        // (The default is every 5 seconds.)
                        options.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 250;
                    });
            })
            .WithMetrics(metrics => metrics
                .AddMeter("System.Net.Http", "System.Net.NameResolution", "Microsoft.AspNetCore.Components", "Microsoft.AspNetCore.Components.Lifecycle")
                .AddOtlpExporter((options, reader) =>
                {
                    Configure(options, "metrics");
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 15_000;
                }))
            .WithLogging(
                logging => logging.AddOtlpExporter(options =>
                {
                    Configure(options, "logs");
                    options.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 250;
                }),
                options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = false;
                });

        return builder;
    }

    /// <summary>
    /// A Blazor WebAssembly host does not run hosted services, which is how the SDK normally starts
    /// itself, so create the providers explicitly (this is what starts the exporters).
    /// </summary>
    public static WebAssemblyHost StartTelemetry(this WebAssemblyHost host)
    {
        _tracerProvider = host.Services.GetRequiredService<TracerProvider>();
        _meterProvider = host.Services.GetRequiredService<MeterProvider>();
        _loggerProvider = host.Services.GetService<LoggerProvider>();

        // Flush when the page is hidden or unloaded (wwwroot/js/telemetry.js)
        try
        {
            ((IJSInProcessRuntime)host.Services.GetRequiredService<IJSRuntime>()).InvokeVoid("grotsTelemetry.registerFlush");
        }
        catch (JSException)
        {
            // Without the flush, telemetry is still sent, just not at the very moment the page goes away.
        }

        return host;
    }

    private static TracerProvider? _tracerProvider;
    private static MeterProvider? _meterProvider;
    private static LoggerProvider? _loggerProvider;

    /// <summary>
    /// Sends everything that is queued now. Called by the page just as it is hidden or unloaded. The flushes are asked to return
    /// immediately: waiting for the export to finish would block the browser's only thread, which is what has to send it.
    /// </summary>
    [JSInvokable]
    public static void FlushTelemetry()
    {
        _tracerProvider?.ForceFlush(timeoutMilliseconds: 1_000);
        _loggerProvider?.ForceFlush(timeoutMilliseconds: 1_000);
        _meterProvider?.ForceFlush(timeoutMilliseconds: 1_000);
    }
}
