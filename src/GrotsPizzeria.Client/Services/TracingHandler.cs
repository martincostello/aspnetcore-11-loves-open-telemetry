using System.Diagnostics;

namespace GrotsPizzeria.Client.Services;

/// <summary>
/// Gives the browser's HttpClient calls a client span and propagates the trace to the server.
/// <para>
/// .NET's own HttpClient tracing is built into the handler that sockets-based platforms use, and a browser does not use
/// it (requests go through fetch), so without this there would be no client spans and no traceparent header: the server's
/// spans would start new traces instead of continuing the one the user's click started. The span follows the HTTP
/// semantic conventions for clients; the HTTP client metrics are already emitted by HttpClient itself.
/// </para>
/// </summary>
public sealed class TracingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Source.StartActivity(request.Method.Method, ActivityKind.Client);

        if (activity is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var uri = request.RequestUri!;
        activity.SetTag("http.request.method", request.Method.Method);
        activity.SetTag("server.address", uri.Host);
        activity.SetTag("server.port", uri.Port);
        activity.SetTag("url.full", uri.GetLeftPart(UriPartial.Path)); // no query string: it could carry something sensitive

        // The W3C traceparent (and tracestate) headers, so the BFF continues this trace
        DistributedContextPropagator.Current.Inject(
            activity,
            request,
            static (carrier, name, value) =>
            {
                if (carrier is HttpRequestMessage message)
                {
                    message.Headers.Remove(name);
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            });

        try
        {
            var response = await base.SendAsync(request, cancellationToken);

            activity.SetTag("http.response.status_code", (int)response.StatusCode);

            if ((int)response.StatusCode >= 400)
            {
                activity.SetTag("error.type", ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
                activity.SetStatus(ActivityStatusCode.Error);
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            activity.SetTag("error.type", ex.GetType().FullName);
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
