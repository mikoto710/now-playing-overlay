namespace NowPlayingOverlay.Host.Media;

internal sealed record SourceKey
{
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
}
