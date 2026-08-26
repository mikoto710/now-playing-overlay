namespace NowPlayingOverlay.Host.Media.Sources;

internal enum SourceProvider
{
    WindowsMedia,
    SpotifyApi,
    ExternalPush,
}

internal static class SourceProviderExtensions
{
    public static string ToProtocolValue(this SourceProvider provider)
    {
        var value = provider switch
        {
            SourceProvider.WindowsMedia => "windows-media",
            SourceProvider.SpotifyApi => "spotify-api",
            SourceProvider.ExternalPush => "external-push",
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "Source provider is invalid."),
        };

        return SourceProviderProtocolToken.EnsureCanonical(value);
    }

    public static string ToDisplayName(this SourceProvider provider)
    {
        return provider switch
        {
            SourceProvider.WindowsMedia => "Windows Media",
            SourceProvider.SpotifyApi => "Spotify API",
            SourceProvider.ExternalPush => "Browser Player",
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "Source provider is invalid."),
        };
    }
}

internal static class SourceProviderProtocolToken
{
    public const int MaximumLength = 64;

    public static bool IsCanonical(string? value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaximumLength
            || value[0] is < 'a' or > 'z'
            || value[^1] == '-')
        {
            return false;
        }

        var previousWasHyphen = false;
        foreach (var character in value)
        {
            if (character == '-')
            {
                if (previousWasHyphen)
                {
                    return false;
                }

                previousWasHyphen = true;
                continue;
            }

            if (character is not (>= 'a' and <= 'z')
                && character is not (>= '0' and <= '9'))
            {
                return false;
            }

            previousWasHyphen = false;
        }

        return true;
    }

    public static string EnsureCanonical(string value)
    {
        return IsCanonical(value)
            ? value
            : throw new InvalidOperationException("Source provider protocol token is not canonical.");
    }
}
