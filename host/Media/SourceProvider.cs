namespace NowPlayingOverlay.Host.Media;

internal enum SourceProvider
{
    WindowsMedia,
    SpotifyApi,
}

internal static class SourceProviderExtensions
{
    public static string ToProtocolValue(this SourceProvider provider)
    {
        return provider switch
        {
            SourceProvider.WindowsMedia => "windows-media",
            SourceProvider.SpotifyApi => "spotify-api",
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "Source provider is invalid."),
        };
    }

    public static string ToDisplayName(this SourceProvider provider)
    {
        return provider switch
        {
            SourceProvider.WindowsMedia => "Windows Media",
            SourceProvider.SpotifyApi => "Spotify API",
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "Source provider is invalid."),
        };
    }
}
