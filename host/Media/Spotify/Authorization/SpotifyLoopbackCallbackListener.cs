using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace NowPlayingOverlay.Host.Media.Spotify.Authorization;

internal sealed class SpotifyLoopbackCallbackListener : IDisposable
{
    private const int MaximumHeaderBytes = 16 * 1024;

    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();
    private readonly TcpListener _listener;
    private int _completed;
    private bool _disposed;

    public SpotifyLoopbackCallbackListener()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(backlog: 1);
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        RedirectUri = new Uri($"http://127.0.0.1:{endpoint.Port}/callback");
    }

    public Uri RedirectUri { get; }

    public async Task<string> WaitForAuthorizationCodeAsync(
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            throw new InvalidOperationException("The Spotify authorization callback is single-use.");
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(timeoutCancellation.Token);
            if (client.Client.RemoteEndPoint is not IPEndPoint remoteEndpoint
                || !IPAddress.IsLoopback(remoteEndpoint.Address))
            {
                throw new SpotifyAuthorizationException("Spotify callback was not received from loopback.");
            }

            await using var stream = client.GetStream();
            try
            {
                var target = await ReadRequestTargetAsync(stream, timeoutCancellation.Token);
                var callback = ParseCallback(target);
                if (!FixedTimeEquals(expectedState, callback.State))
                {
                    throw new SpotifyAuthorizationException(
                        "Spotify authorization callback state did not match.");
                }

                if (callback.ErrorCode is not null)
                {
                    throw new SpotifyAuthorizationException(
                        $"Spotify authorization failed with '{callback.ErrorCode}'.",
                        callback.ErrorCode);
                }

                if (string.IsNullOrWhiteSpace(callback.AuthorizationCode)
                    || callback.AuthorizationCode.Length > 8192
                    || callback.AuthorizationCode.Any(char.IsControl))
                {
                    throw new SpotifyAuthorizationException(
                        "Spotify authorization callback did not contain a valid code.");
                }

                await WriteResponseAsync(
                    stream,
                    HttpStatusCode.OK,
                    "Spotify connection completed. You can close this browser tab and return to Now Playing Overlay.",
                    timeoutCancellation.Token);
                return callback.AuthorizationCode;
            }
            catch (SpotifyAuthorizationException)
            {
                await WriteResponseAsync(
                    stream,
                    HttpStatusCode.BadRequest,
                    "Spotify connection was not completed. Return to Now Playing Overlay and try again.",
                    CancellationToken.None);
                throw;
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SpotifyAuthorizationException(
                "Spotify authorization callback timed out.",
                innerException: error);
        }
        finally
        {
            _listener.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener.Stop();
    }

    private static async Task<string> ReadRequestTargetAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[2048];
        while (buffer.Length < MaximumHeaderBytes)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaximumHeaderBytes)
            {
                throw new SpotifyAuthorizationException(
                    "Spotify callback HTTP headers exceeded the supported size.");
            }

            if (buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)).IndexOf(HeaderTerminator) >= 0)
            {
                break;
            }
        }

        var header = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
        var terminator = header.IndexOf(HeaderTerminator);
        if (terminator < 0)
        {
            throw new SpotifyAuthorizationException("Spotify callback HTTP headers were invalid.");
        }

        var firstLineEnd = header.IndexOf("\r\n"u8);
        if (firstLineEnd <= 0)
        {
            throw new SpotifyAuthorizationException("Spotify callback request line was invalid.");
        }

        var requestLine = Encoding.ASCII.GetString(header[..firstLineEnd]);
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || !string.Equals(parts[0], "GET", StringComparison.Ordinal)
            || parts[2] is not "HTTP/1.1" and not "HTTP/1.0")
        {
            throw new SpotifyAuthorizationException("Spotify callback request was invalid.");
        }

        return parts[1];
    }

    private CallbackResult ParseCallback(string requestTarget)
    {
        if (!requestTarget.StartsWith("/", StringComparison.Ordinal)
            || !Uri.TryCreate(RedirectUri, requestTarget, out var requestUri)
            || !string.Equals(requestUri.Scheme, RedirectUri.Scheme, StringComparison.Ordinal)
            || !string.Equals(requestUri.Host, RedirectUri.Host, StringComparison.Ordinal)
            || requestUri.Port != RedirectUri.Port
            || !string.Equals(requestUri.AbsolutePath, RedirectUri.AbsolutePath, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(requestUri.Fragment))
        {
            throw new SpotifyAuthorizationException("Spotify callback path was invalid.");
        }

        var parameters = ParseQuery(requestUri.Query);
        parameters.TryGetValue("state", out var state);
        parameters.TryGetValue("code", out var code);
        parameters.TryGetValue("error", out var error);
        if (error is { Length: > 128 } || error?.Any(char.IsControl) == true)
        {
            throw new SpotifyAuthorizationException("Spotify callback error was invalid.");
        }

        return new CallbackResult(state, code, error);
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var encodedName = separator < 0 ? pair : pair[..separator];
            var encodedValue = separator < 0 ? string.Empty : pair[(separator + 1)..];
            string name;
            string value;
            try
            {
                name = Uri.UnescapeDataString(encodedName.Replace('+', ' '));
                value = Uri.UnescapeDataString(encodedValue.Replace('+', ' '));
            }
            catch (UriFormatException error)
            {
                throw new SpotifyAuthorizationException(
                    "Spotify callback query was invalid.",
                    innerException: error);
            }

            if (!result.TryAdd(name, value))
            {
                throw new SpotifyAuthorizationException("Spotify callback query contained duplicates.");
            }
        }

        return result;
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        HttpStatusCode statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(
            $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Now Playing Overlay</title></head><body><p>{message}</p></body></html>");
        var reason = statusCode == HttpStatusCode.OK ? "OK" : "Bad Request";
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)statusCode} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException)
        {
            // The callback result is authoritative even if the browser closes before reading the page.
        }
    }

    private sealed record CallbackResult(
        string? State,
        string? AuthorizationCode,
        string? ErrorCode);
}
