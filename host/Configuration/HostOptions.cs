namespace NowPlayingOverlay.Host.Configuration;

internal sealed record HostOptions
{
    public const string SectionName = "Host";
    public const string AllowedHost = "127.0.0.1";
    public const int DefaultPort = 10598;

    public int Port { get; init; } = DefaultPort;

    public int MaximumConcurrentConnections { get; init; } = 32;

    public int MaximumSseConnections { get; init; } = 4;

    public int MaximumRequestHeaderCount { get; init; } = 32;

    public int MaximumRequestHeadersTotalSize { get; init; } = 16 * 1024;

    public TimeSpan RequestHeadersTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan SseHeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan PortRebindGracePeriod { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port));
        }

        if (MaximumConcurrentConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumConcurrentConnections));
        }

        if (MaximumSseConnections is <= 0 || MaximumSseConnections > MaximumConcurrentConnections)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSseConnections));
        }

        if (MaximumRequestHeaderCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumRequestHeaderCount));
        }

        if (MaximumRequestHeadersTotalSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumRequestHeadersTotalSize));
        }

        if (RequestHeadersTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestHeadersTimeout));
        }

        if (KeepAliveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(KeepAliveTimeout));
        }

        if (SseHeartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(SseHeartbeatInterval));
        }

        if (PortRebindGracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PortRebindGracePeriod));
        }

    }
}
