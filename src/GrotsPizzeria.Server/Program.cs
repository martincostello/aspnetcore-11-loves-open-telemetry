using System.Security.Claims;
using GrotsPizzeria.Server;
using GrotsPizzeria.Server.Auth;
using GrotsPizzeria.Server.Downstream;
using GrotsPizzeria.Server.Endpoints;
using GrotsPizzeria.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// `dotnet GrotsPizzeria.Server.dll healthcheck` is what the container's health
// check runs: the aspnet image has no curl, and this keeps it that way.
if (args is ["healthcheck"])
{
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

    try
    {
        using var response = await probe.GetAsync(Environment.GetEnvironmentVariable("HEALTHCHECK_URL") ?? "http://localhost:8080/healthz");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
    catch (TaskCanceledException)
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry:
// ASP.NET Core and .NET emit spans and metrics using the OpenTelemetry semantic conventions natively, so the
// SDK only needs to be told which sources to listen to. The one exception is EF Core, which has no built-in tracing.
// The exporter is configured entirely from the standard OTEL_* environment variables
// (OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_EXPORTER_OTLP_PROTOCOL, OTEL_SERVICE_NAME, ...).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("grots-web"))
    .WithTracing(tracing => tracing
        .SetSampler(new HealthCheckSampler(ConfiguredSampler.Create(builder.Configuration)))
        .AddSource("Microsoft.AspNetCore")
        .AddSource("System.Net.Http")
        .AddProcessor<BaggageActivityProcessor>()
        .AddEntityFrameworkCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("Microsoft.AspNetCore.*")
        .AddMeter("System.Net.Http", "System.Net.NameResolution", "System.Runtime")
        .AddMeter(Telemetry.Name))
    .WithLogging(logging => logging.AddProcessor(new DropOriginalFormatProcessor()), options =>
    {
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = false;
    })
    .UseOtlpExporter();

// Authentication: ASP.NET Core Identity (EF Core + SQLite) issuing a cookie
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Users") ?? "Data Source=grots.db"));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies(cookies => cookies.ApplicationCookie!.Configure(options =>
    {
        options.Cookie.Name = "grots.auth";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);

        // This is an API for a SPA: answer with status codes rather than redirecting to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }));
builder.Services.AddAuthorization();

// Keep the cookie encryption keys somewhere that survives restarts when asked to (the container uses a volume).
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

// The polyglot microservices
// Each base URL comes from an environment variable and each call carries a JWT (see docs/service-contract.md).
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ServiceTokenIssuer>();
builder.Services.AddTransient<ServiceTokenHandler>();
builder.Services.AddSingleton<Telemetry>();

AddService<MenuClient>(builder, "MENU_URL", 8081);
AddService<OrdersClient>(builder, "ORDERS_URL", 8082);
AddService<DeliveryClient>(builder, "DELIVERY_URL", 8084);

var app = builder.Build();

await InitializeDatabaseAsync(app.Services);

// The Blazor WebAssembly app is served by this same process (it is the BFF).
app.UseBlazorFrameworkFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // The app's own CSS and scripts are not fingerprinted, so without this a browser may keep using an old copy for a while
    // after a change. "no-cache" means "check with the server first": the ETag makes that a cheap 304 when nothing changed.
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

app.UseAuthentication();
app.UseAuthorization();

// Settings for the UI: where its "See telemetry" link goes (the provisioned Grafana dashboard by default),
// and where the browser should send its own OTLP (unset means the browser sends no telemetry).
app.MapGet("/api/config", (IConfiguration configuration) =>
    new ClientConfig(
        configuration["TELEMETRY_URL"] ?? "http://localhost:3000/d/grots-http-semconv",
        configuration["BROWSER_OTLP_ENDPOINT"]));

app.MapGet("/healthz", () => Results.Json(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapPizzeriaEndpoints();

// Unknown /api routes are 404s, not the SPA.
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
return 0;

static void AddService<TClient>(WebApplicationBuilder builder, string name, int defaultPort)
    where TClient : class
{
    var url = builder.Configuration[name] ?? $"http://localhost:{defaultPort}";

    builder.Services
        .AddHttpClient<TClient>(client => client.BaseAddress = new Uri(url.TrimEnd('/') + "/"))
        .AddHttpMessageHandler<ServiceTokenHandler>();
}

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();

    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();

    // A demo account so there is something to log in with straight away.
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    if (await users.FindByNameAsync("grot") is null)
    {
        var demo = new ApplicationUser { UserName = "grot", DisplayName = "Grot" };
        (await users.CreateAsync(demo, "Grot!Pizza1")).ThrowOnFailure();
        await users.AddClaimAsync(demo, new Claim(ClaimTypes.GivenName, demo.DisplayName));
    }
}
