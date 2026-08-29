namespace NowPlayingOverlay.Host.Media.Sources;

internal sealed record SourceKey
{
    private const string SpotifyCurrentAccountInstanceId = "current-account";
    private const string ExternalPushInstanceId = "default";

    public SourceKey(SourceProvider provider, string instanceId)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (instanceId.Length > 1024 || instanceId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Source instance ID must be at most 1024 non-control characters.",
                nameof(instanceId));
        }

        Provider = provider;
        InstanceId = instanceId;
    }

    public SourceProvider Provider { get; }

    public string InstanceId { get; }

    public static SourceKey WindowsMedia(string sourceAppUserModelId)
    {
        return new SourceKey(SourceProvider.WindowsMedia, sourceAppUserModelId);
    }

    public static SourceKey SpotifyApi()
    {
        return new SourceKey(SourceProvider.SpotifyApi, SpotifyCurrentAccountInstanceId);
    }

    public static SourceKey ExternalPush()
    {
        return new SourceKey(SourceProvider.ExternalPush, ExternalPushInstanceId);
    }

    public static SourceKey WindowTitle(string targetInstanceId)
    {
        return new SourceKey(SourceProvider.WindowTitle, targetInstanceId);
    }
}
