namespace NowPlayingOverlay.Host.Media.Spotify;

internal sealed class SpotifyHttpResponse(HttpResponseMessage message, TimeProvider clock) : IDisposable
{
    private static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(5);
    private readonly long _headersReceivedAt = clock.GetTimestamp();

    public HttpResponseMessage Message { get; } = message;

    public async Task<byte[]> ReadBodyAsync(int maximumBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Message.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("Spotify response exceeded the allowed size.");
        }

        var remaining = BodyTimeout - clock.GetElapsedTime(_headersReceivedAt);
        if (remaining <= TimeSpan.Zero)
        {
            throw new HttpRequestException("Spotify response body timed out.");
        }

        using var deadline = new CancellationTokenSource(remaining, clock);
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await using var source = await Message.Content.ReadAsStreamAsync(reading.Token);
            using var destination = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var count = await source.ReadAsync(buffer, reading.Token);
                reading.Token.ThrowIfCancellationRequested();
                if (count == 0)
                {
                    return destination.ToArray();
                }

                if (destination.Length + count > maximumBytes)
                {
                    throw new InvalidDataException("Spotify response exceeded the allowed size.");
                }

                destination.Write(buffer, 0, count);
            }
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("Spotify response body timed out.", error);
        }
        catch (IOException error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new HttpRequestException("Spotify response body was interrupted.", error);
        }
    }

    public void Dispose() => Message.Dispose();
}
