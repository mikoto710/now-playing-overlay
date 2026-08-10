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

    public SessionSourceKind SessionSource { get; init; } = SessionSourceKind.Windows;

    public bool RunFakeScenario { get; init; }

    public WebAssetMode WebAssetMode { get; init; } = WebAssetMode.Embedded;

    public string? DevelopmentWebRoot { get; init; }

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

        if (!Enum.IsDefined(SessionSource))
        {
            throw new ArgumentOutOfRangeException(nameof(SessionSource));
        }

        if (RunFakeScenario && SessionSource != SessionSourceKind.Fake)
        {
            throw new ArgumentException("The fake scenario requires the fake session source.");
        }

        if (!Enum.IsDefined(WebAssetMode))
        {
            throw new ArgumentOutOfRangeException(nameof(WebAssetMode));
        }

        if (DevelopmentWebRoot is not null && string.IsNullOrWhiteSpace(DevelopmentWebRoot))
        {
            throw new ArgumentException(
                "The development web root must be a non-empty path when provided.",
                nameof(DevelopmentWebRoot));
        }
    }
}
