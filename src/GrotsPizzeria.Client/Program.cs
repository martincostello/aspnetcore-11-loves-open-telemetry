using GrotsPizzeria.Client;
using System.Net.Http.Json;
using GrotsPizzeria.Client.Services;
using GrotsPizzeria.Shared;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

var bootstrap = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
var config = await bootstrap.GetFromJsonAsync<ClientConfig>("api/config");
var telemetryEnabled = Uri.TryCreate(config?.OtlpEndpoint, UriKind.Absolute, out var otlpEndpoint);

if (telemetryEnabled)
{
    builder.AddTelemetry(otlpEndpoint!);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Authorization", LogLevel.Warning);
}

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The BFF serves this app, so the API is always on the same origin (and the auth cookie goes along for free).
builder.Services.AddScoped(_ => new HttpClient(new TracingHandler { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});

builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<CartState>();
builder.Services.AddScoped<MenuState>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ThemeService>();

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CookieAuthenticationStateProvider>());

var host = builder.Build();

if (telemetryEnabled)
{
    host.StartTelemetry();
}

await host.RunAsync();
