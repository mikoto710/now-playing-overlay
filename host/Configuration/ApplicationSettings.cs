using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Configuration;

internal sealed record ApplicationSettings
{
    public int Port { get; init; } = HostOptions.DefaultPort;

    public SourceSelectionSettings Source { get; init; } = new();

    public SpotifyConnectionSettings Spotify { get; init; } = new();

    public AppearanceSettings Appearance { get; init; } = new();

    public void Validate()
    {
        if (Port is < 1 or > 65535)
        {
            throw new InvalidDataException("The configured port must be between 1 and 65535.");
        }

        if (Source is null)
        {
            throw new InvalidDataException("The configured source must not be null.");
        }

        Source.Validate();

        if (Spotify is null)
        {
            throw new InvalidDataException("The configured Spotify connection must not be null.");
        }

        Spotify.Validate();
        if (Source.Provider == SourceProvider.SpotifyApi && Spotify.ClientId is null)
        {
            throw new InvalidDataException("Spotify API cannot be selected without a Client ID.");
        }

        if (Appearance is null)
        {
            throw new InvalidDataException("The configured appearance must not be null.");
        }

        Appearance.Validate();
    }
}

internal sealed record SourceSelectionSettings
{
    public SourceProvider Provider { get; init; } = SourceProvider.WindowsMedia;

    public string? SourceAppUserModelId { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Provider))
        {
            throw new InvalidDataException("The configured source provider is not supported.");
        }

        if (SourceAppUserModelId is not null)
        {
            try
            {
                _ = SourceKey.WindowsMedia(SourceAppUserModelId);
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException("The configured Windows Media source ID is invalid.", error);
            }
        }
    }

    public SourceDescriptor? ToDescriptor()
    {
        Validate();
        return Provider switch
        {
            SourceProvider.WindowsMedia => SourceAppUserModelId is null
                ? null
                : SourceDescriptor.WindowsMedia(SourceAppUserModelId),
            SourceProvider.SpotifyApi => SourceDescriptor.SpotifyApi(),
            _ => throw new InvalidDataException("The configured source provider is not supported."),
        };
    }
}

internal sealed record SpotifyConnectionSettings
{
    public string? ClientId { get; init; }

    public void Validate()
    {
        if (ClientId is null)
        {
            return;
        }

        try
        {
            _ = new SpotifyClientId(ClientId);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("The configured Spotify Client ID is invalid.", error);
        }
    }

    public SpotifyClientId? ToClientId()
    {
        Validate();
        return ClientId is null ? null : new SpotifyClientId(ClientId);
    }
}
