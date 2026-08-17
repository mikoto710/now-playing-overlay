namespace NowPlayingOverlay.Host.Media;

internal enum SourceStatus
{
    Unconfigured,
    Starting,
    Available,
    Unavailable,
    Faulted,
}

internal static class SourceStatusExtensions
{
    public static string ToProtocolValue(this SourceStatus status)
    {
        return status switch
        {
            SourceStatus.Unconfigured => "unconfigured",
            SourceStatus.Starting => "starting",
            SourceStatus.Available => "available",
            SourceStatus.Unavailable => "unavailable",
            SourceStatus.Faulted => "faulted",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Source status is invalid."),
        };
    }
}

internal enum SourceStatusReason
{
    Unconfigured,
    Starting,
    None,
    Missing,
    Ambiguous,
    PlatformUnavailable,
    AuthorizationRequired,
    Forbidden,
    RateLimited,
    NetworkUnavailable,
    ServiceUnavailable,
    Unsupported,
    Stale,
    Faulted,
}

internal sealed record SourceManagerState(
    SourceDescriptor? ActiveSource,
    SourceStatus Status,
    SourceStatusReason Reason)
{
    public static SourceManagerState Unconfigured { get; } =
        new(null, SourceStatus.Unconfigured, SourceStatusReason.Unconfigured);
}

internal sealed record SourceDiscoveryResult(
    IReadOnlyList<SourceDescriptor> Sources,
    SourceManagerState State);
