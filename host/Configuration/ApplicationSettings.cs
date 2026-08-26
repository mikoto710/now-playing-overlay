using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Configuration;

internal sealed record ApplicationSettings
{
    public int Port { get; init; } = HostOptions.DefaultPort;

    public SourceSelectionSettings Source { get; init; } = new();

    public WindowsMediaSettings WindowsMedia { get; init; } = new();

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

        if (WindowsMedia is null)
        {
            throw new InvalidDataException("The configured Windows Media settings must not be null.");
        }

        WindowsMedia.Validate();
        if (Source.Provider == SourceProvider.WindowsMedia
            && !string.Equals(
                Source.InstanceId,
                WindowsMedia.LastInstanceId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The selected Windows Media source must match the remembered Windows Media source.");
        }

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

    public string? InstanceId { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Provider))
        {
            throw new InvalidDataException("The configured source provider is not supported.");
        }

        try
        {
            switch (Provider)
            {
                case SourceProvider.WindowsMedia when InstanceId is not null:
                    _ = SourceKey.WindowsMedia(InstanceId);
                    break;
                case SourceProvider.SpotifyApi when !string.Equals(
                    InstanceId,
                    SourceKey.SpotifyApi().InstanceId,
                    StringComparison.Ordinal):
                    throw new InvalidDataException(
                        "The configured Spotify API source instance is invalid.");
                case SourceProvider.ExternalPush when !string.Equals(
                    InstanceId,
                    SourceKey.ExternalPush().InstanceId,
                    StringComparison.Ordinal):
                    throw new InvalidDataException(
                        "The configured Browser Player instance is invalid.");
            }
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("The configured source instance ID is invalid.", error);
        }
    }

    public SourceDescriptor? ToDescriptor()
    {
        Validate();
        return Provider switch
        {
            SourceProvider.WindowsMedia => InstanceId is null
                ? null
                : SourceDescriptor.WindowsMedia(InstanceId),
            SourceProvider.SpotifyApi => SourceDescriptor.SpotifyApi(),
            SourceProvider.ExternalPush => SourceDescriptor.ExternalPush(),
            _ => throw new InvalidDataException("The configured source provider is not supported."),
        };
    }

    public static SourceSelectionSettings WindowsMedia(string? instanceId)
    {
        return new SourceSelectionSettings
        {
            Provider = SourceProvider.WindowsMedia,
            InstanceId = instanceId,
        };
    }

    public static SourceSelectionSettings SpotifyApi()
    {
        return new SourceSelectionSettings
        {
            Provider = SourceProvider.SpotifyApi,
            InstanceId = SourceKey.SpotifyApi().InstanceId,
        };
    }

    public static SourceSelectionSettings ExternalPush()
    {
        return new SourceSelectionSettings
        {
            Provider = SourceProvider.ExternalPush,
            InstanceId = SourceKey.ExternalPush().InstanceId,
        };
    }
}

internal sealed record WindowsMediaSettings
{
    public string? LastInstanceId { get; init; }

    public void Validate()
    {
        if (LastInstanceId is null)
        {
            return;
        }

        try
        {
            _ = SourceKey.WindowsMedia(LastInstanceId);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException(
                "The remembered Windows Media source instance ID is invalid.",
                error);
        }
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
