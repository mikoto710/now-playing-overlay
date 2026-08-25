namespace NowPlayingOverlay.Host.Media.Sources;

internal sealed record SourceDescriptor
{
    public SourceDescriptor(SourceKey key, string displayName)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (displayName.Length > 1024 || displayName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Source display name must be at most 1024 non-control characters.",
                nameof(displayName));
        }

        DisplayName = displayName;
    }

    public SourceKey Key { get; }

    public string DisplayName { get; }

    public static SourceDescriptor WindowsMedia(
        string sourceAppUserModelId,
        string? displayName = null)
    {
        return new SourceDescriptor(
            SourceKey.WindowsMedia(sourceAppUserModelId),
            string.IsNullOrWhiteSpace(displayName) ? sourceAppUserModelId : displayName);
    }

    public static SourceDescriptor SpotifyApi()
    {
        return new SourceDescriptor(SourceKey.SpotifyApi(), SourceProvider.SpotifyApi.ToDisplayName());
    }

    public static SourceDescriptor ExternalPush()
    {
        return new SourceDescriptor(
            SourceKey.ExternalPush(),
            SourceProvider.ExternalPush.ToDisplayName());
    }
}
