using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;

namespace NowPlayingOverlay.Host.Hosting;

/// <summary>
/// Enforces request-wide loopback security limits before dispatching a request. The listener owns
/// the cancellation token; this type always closes the response after the selected handler exits.
/// </summary>
internal sealed class OverlayHttpRequestDispatcher
{
    private readonly HostOptions _options;
    private readonly ConnectionLimiter _requestLimiter;
    private readonly OverlayEndpointHandlers _endpoints;
    private readonly ILogger _logger;

    public OverlayHttpRequestDispatcher(
        HostOptions options,
        ConnectionLimiter requestLimiter,
        OverlayEndpointHandlers endpoints,
        ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _requestLimiter = requestLimiter ?? throw new ArgumentNullException(nameof(requestLimiter));
        _endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        var response = context.Response;
        try
        {
            PrepareCommonResponse(response);
            if (!_requestLimiter.TryAcquire(out var requestLease))
            {
                response.StatusCode = 503;
                response.Headers[HttpResponseHeader.RetryAfter] = "1";
                response.ContentLength64 = 0;
                return;
            }

            using (requestLease)
            {
                if (!HasAllowedHeaders(context.Request))
                {
                    response.StatusCode = 431;
                    response.ContentLength64 = 0;
                    return;
                }

                if (!HasAllowedHost(context.Request))
                {
                    response.StatusCode = 400;
                    response.ContentLength64 = 0;
                    return;
                }

                await _endpoints.DispatchAsync(context, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is IOException or HttpListenerException)
        {
            _logger.LogDebug(
                "The loopback client disconnected while a response was in progress. Error type {ErrorType}, HRESULT {ErrorHResult}.",
                error.GetType().Name,
                error.HResult);
        }
        catch (Exception error)
        {
            _logger.LogError(
                "The loopback HTTP request failed. {Diagnostic}",
                SanitizedExceptionDiagnostics.Create(error));
            TrySetInternalServerError(response);
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception error) when (error is IOException or HttpListenerException or ObjectDisposedException)
            {
            }
        }
    }

    private bool HasAllowedHeaders(HttpListenerRequest request)
    {
        if (request.Headers.Count > _options.MaximumRequestHeaderCount)
        {
            return false;
        }

        var totalBytes = 0;
        foreach (var name in request.Headers.AllKeys)
        {
            if (name is null)
            {
                continue;
            }

            totalBytes = checked(totalBytes + Encoding.UTF8.GetByteCount(name) + 2);
            foreach (var value in request.Headers.GetValues(name) ?? [])
            {
                totalBytes = checked(totalBytes + Encoding.UTF8.GetByteCount(value) + 2);
            }

            if (totalBytes > _options.MaximumRequestHeadersTotalSize)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAllowedHost(HttpListenerRequest request)
    {
        var host = request.Headers["Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var separator = host.LastIndexOf(':');
        var hostName = separator < 0 ? host : host[..separator];
        var port = separator < 0 ? null : host[(separator + 1)..];
        return string.Equals(hostName, HostOptions.AllowedHost, StringComparison.Ordinal)
            && (port is null
                || int.TryParse(port, out var parsedPort) && parsedPort is >= 1 and <= 65535);
    }

    private static void PrepareCommonResponse(HttpListenerResponse response)
    {
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers[HttpResponseHeader.Server] = string.Empty;
    }

    private static void TrySetInternalServerError(HttpListenerResponse response)
    {
        try
        {
            response.StatusCode = 500;
            response.ContentLength64 = 0;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
