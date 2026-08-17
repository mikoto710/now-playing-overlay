using NowPlayingOverlay.Host.Media.Sources;

namespace NowPlayingOverlay.Host.Configuration;

internal sealed record ApplicationSettings
{
    public int Port { get; init; } = HostOptions.DefaultPort;

    public SourceSelectionSettings Source { get; init; } = new();

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
        if (Provider != SourceProvider.WindowsMedia)
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
        return SourceAppUserModelId is null
            ? null
            : SourceDescriptor.WindowsMedia(SourceAppUserModelId);
    }
}
