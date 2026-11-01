using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace GrotsPizzeria.EndToEndTests;

/// <summary>
/// A stand-in OTLP/HTTP collector for the browser to send its telemetry to when the tests start the stack themselves.
/// It allows cross-origin requests (as LGTM's collector does) and remembers what arrived.
/// </summary>
public sealed class OtlpSink : IAsyncDisposable
{
    private readonly WebApplication _app;

    private OtlpSink(WebApplication app, Uri address)
    {
        _app = app;
        Address = address;
    }

    public Uri Address { get; }

    /// <summary>The OTLP requests received so far: the signal ("traces", "metrics" or "logs") and the raw protobuf body.</summary>
    public ConcurrentQueue<(string Signal, byte[] Body)> Received { get; } = new();

    /// <summary>Whether a request for the signal has been received whose body contains the text (protobuf strings are plain UTF-8).</summary>
    public bool Contains(string signal, string text)
    {
        var needle = System.Text.Encoding.UTF8.GetBytes(text);
        return Received.Any(r => r.Signal == signal && r.Body.AsSpan().IndexOf(needle) >= 0);
    }

    public static async Task<OtlpSink> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();
        app.UseCors();

        OtlpSink? sink = null;
        app.MapPost("/v1/{signal}", async (string signal, HttpRequest request) =>
        {
            using var body = new MemoryStream();
            await request.Body.CopyToAsync(body);
            sink!.Received.Enqueue((signal, body.ToArray()));
            return Results.Ok();
        });

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        sink = new OtlpSink(app, new Uri(address));
        return sink;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
