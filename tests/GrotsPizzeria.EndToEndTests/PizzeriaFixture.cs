using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>
/// Starts the whole sample once for all of the tests, and a Chromium to drive it.
/// <para>
/// By default the stack is started through the Aspire AppHost (this needs Docker, as the Node.js, Java, Python and
/// Go services run from their Dockerfiles). Set <c>E2E_BASE_URL</c> (e.g. <c>http://localhost:8080</c>) to test an
/// already running stack instead, such as the one from <c>docker compose up</c> (start it with
/// <c>CHECKOUT_FAILURE_RATE=0 SPECIAL_OFFERS=off</c> to stop the checkout service's deliberate random failures and the changing special offers breaking the tests).
/// </para>
/// </summary>
public sealed class PizzeriaFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private DistributedApplication? _app;
    private OtlpSink? _otlpSink;
    private IPlaywright? _playwright;

    public Uri BaseAddress { get; private set; } = default!;

    /// <summary>Where the browser sends its telemetry when the tests start the stack themselves (otherwise <see langword="null"/>).</summary>
    public OtlpSink? OtlpSink => _otlpSink;

    public IBrowser Browser { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        // The first load of a Blazor WebAssembly app is slow on a cold start.
        Assertions.SetDefaultExpectTimeout(30_000);

        if (Environment.GetEnvironmentVariable("E2E_BASE_URL") is { Length: > 0 } external)
        {
            BaseAddress = new Uri(external);
        }
        else
        {
            using var cts = new CancellationTokenSource(StartupTimeout);

            // The checkout service randomly fails some payments by design; keep the tests deterministic.
            Environment.SetEnvironmentVariable("CHECKOUT_FAILURE_RATE", "0");

            // Likewise the special offers that change the prices every 5 minutes (the tests that look at them provide their own menu).
            Environment.SetEnvironmentVariable("SPECIAL_OFFERS", "off");

            // The browser sends its telemetry straight to a collector; give it one that the tests can look inside.
            _otlpSink = await OtlpSink.StartAsync();
            Environment.SetEnvironmentVariable("BROWSER_OTLP_ENDPOINT", _otlpSink.Address.AbsoluteUri);

            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.GrotsPizzeria_AppHost>(cts.Token);
            _app = await builder.BuildAsync(cts.Token);

            await _app.StartAsync(cts.Token);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("grots-web", cts.Token);

            BaseAddress = _app.GetEndpoint("grots-web", "http");
        }

        _playwright = await Playwright.CreateAsync();

        // Chromium by default; E2E_BROWSER_CHANNEL=chrome (or msedge) uses an installed browser instead.
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Channel = Environment.GetEnvironmentVariable("E2E_BROWSER_CHANNEL"),
            Headless = Environment.GetEnvironmentVariable("E2E_HEADED") is not "true",
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (_otlpSink is not null)
        {
            await _otlpSink.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PizzeriaCollection : ICollectionFixture<PizzeriaFixture>
{
    public const string Name = "Grot's Pizzeria";
}
