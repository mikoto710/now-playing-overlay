using System.Net;
using System.Text;

namespace NowPlayingOverlay.Host.Hosting;

/// <summary>
/// Applies the shared response contract used by the explicit loopback routes.
/// </summary>
internal static class LoopbackHttpResponseWriter
{
    public static async Task HandleGetAndHeadAsync(
        HttpListenerContext context,
        string contentType,
        string cacheControl,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken,
        string? contentSecurityPolicy = null,
        int statusCode = 200)
    {
        if (!IsGetOrHead(context.Request))
        {
            WriteMethodNotAllowed(context.Response, "GET, HEAD");
            return;
        }

        if (contentSecurityPolicy is not null)
        {
            context.Response.Headers["Content-Security-Policy"] = contentSecurityPolicy;
        }

        context.Response.StatusCode = statusCode;
        await WriteBodyAsync(context, contentType, cacheControl, bytes, cancellationToken);
    }

    public static async Task WriteBodyAsync(
        HttpListenerContext context,
        string contentType,
        string cacheControl,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        context.Response.ContentType = contentType;
        context.Response.Headers[HttpResponseHeader.CacheControl] = cacheControl;
        context.Response.ContentLength64 = bytes.Length;
        if (!IsMethod(context.Request, "HEAD"))
        {
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        }
    }

    public static async Task WriteUtf8Async(
        HttpListenerResponse response,
        string value,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        await response.OutputStream.FlushAsync(cancellationToken);
    }

    public static void WriteMethodNotAllowed(HttpListenerResponse response, string allow)
    {
        response.StatusCode = 405;
        response.Headers[HttpResponseHeader.Allow] = allow;
        response.ContentLength64 = 0;
    }

    public static bool IsGetOrHead(HttpListenerRequest request)
    {
        return IsMethod(request, "GET") || IsMethod(request, "HEAD");
    }

    public static bool IsMethod(HttpListenerRequest request, string method)
    {
        return string.Equals(request.HttpMethod, method, StringComparison.OrdinalIgnoreCase);
    }
}
