using System.Security.Cryptography;

// Runs the whole pizzeria under Aspire: `aspire run` (or F5 in Visual Studio / VS Code).
// The BFF runs as a project; the Node.js, Java, Python and Go services are built from their Dockerfiles
// (so nothing but Docker and the .NET SDK is needed), and telemetry from all of them lands in the Aspire dashboard.
var builder = DistributedApplication.CreateBuilder(args);

// W3C trace context (traceparent and tracestate, which carry the sampling decision) and baggage, between all the applications.
const string Propagators = "tracecontext,baggage";

// The shared HS256 key that secures service-to-service calls (see docs/service-contract.md).
// Generated per run; Aspire keeps it out of logs and the dashboard.
var jwtKey = builder.AddParameter("jwt-signing-key", () => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), secret: true);

var menu = AddService("grots-menu", "menu", 8081)
    .WithEnvironment("SPECIAL_OFFERS", builder.Configuration["SPECIAL_OFFERS"] ?? "on");

var checkout = AddService("grots-checkout", "checkout", 8083)
    .WithEnvironment("CHECKOUT_FAILURE_RATE", builder.Configuration["CHECKOUT_FAILURE_RATE"] ?? "0.05");

var delivery = AddService("grots-delivery", "delivery", 8084);

var orders = AddService("grots-orders", "orders", 8082, debugSampler: "grots_always_on")
    .WithEnvironment("MENU_URL", menu.GetEndpoint("http"))
    .WithEnvironment("CHECKOUT_URL", checkout.GetEndpoint("http"))
    .WithEnvironment("DELIVERY_URL", delivery.GetEndpoint("http"))
    .WaitFor(menu)
    .WaitFor(checkout)
    .WaitFor(delivery);

// Where the browser (the Blazor client runs the OpenTelemetry SDK too) sends its telemetry: an explicit
// BROWSER_OTLP_ENDPOINT, or else the Aspire dashboard's OTLP/HTTP endpoint. The dashboard allows the app's origin
// automatically, and is run without OTLP authentication (see launchSettings.json) because a browser has no secret to
// authenticate with. Without either, the browser sends no telemetry.
var browserOtlp = builder.Configuration["BROWSER_OTLP_ENDPOINT"]
    ?? builder.Configuration["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"]
    ?? builder.Configuration["DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"];

var web = builder.AddProject<Projects.GrotsPizzeria_Server>("grots-web")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/healthz")
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .WithEnvironment("JWT_SIGNING_KEY", jwtKey)
    .WithEnvironment("OTEL_PROPAGATORS", Propagators)
    .WithEnvironment("OTEL_SEMCONV_STABILITY_OPT_IN", "database")
    .WithEnvironment("MENU_URL", menu.GetEndpoint("http"))
    .WithEnvironment("ORDERS_URL", orders.GetEndpoint("http"))
    .WithEnvironment("DELIVERY_URL", delivery.GetEndpoint("http"))
    .WithEnvironment("TELEMETRY_URL", builder.Configuration["ASPNETCORE_URLS"]?.Split(';')[0] ?? "http://localhost:15140") // the Aspire dashboard
    .WaitFor(orders);

AlwaysSampleWhenDebugging(web);

if (browserOtlp is not null)
{
    web.WithEnvironment("BROWSER_OTLP_ENDPOINT", browserOtlp);
}

builder.Build().Run();

// When running (and debugging) under Aspire, record every trace. The SDKs' default sampler (parentbased_always_on) already
// samples every root span, but it also honours an upstream caller's decision not to sample, which can leave gaps in a
// trace you are trying to look at. OTEL_TRACES_SAMPLER is understood by the .NET, Java, Node.js, Python and Go SDKs.
void AlwaysSampleWhenDebugging<T>(IResourceBuilder<T> resource, string sampler = "always_on")
    where T : IResourceWithEnvironment
{
    if (builder.ExecutionContext.IsRunMode)
    {
        resource.WithEnvironment("OTEL_TRACES_SAMPLER", sampler);
    }
}

IResourceBuilder<ContainerResource> AddService(string name, string directory, int port, string debugSampler = "always_on")
{
    var service = builder.AddDockerfile(name, $"../../services/{directory}")
        .WithHttpEndpoint(targetPort: port, name: "http")
        .WithHttpHealthCheck("/healthz")
        .WithEnvironment("JWT_SIGNING_KEY", jwtKey)
        .WithEnvironment("OTEL_PROPAGATORS", Propagators)
        .WithOtlpExporter(OtlpProtocol.HttpProtobuf);

    AlwaysSampleWhenDebugging(service, debugSampler);
    return service;
}
