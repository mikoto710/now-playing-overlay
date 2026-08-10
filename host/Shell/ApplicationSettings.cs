using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Shell;

using OverlayHostOptions = Configuration.HostOptions;

internal sealed record ApplicationSettings
{
    public int Port { get; init; } = OverlayHostOptions.DefaultPort;

    public void Validate()
    {
        if (Port is < 1 or > 65535)
        {
            throw new InvalidDataException("The configured port must be between 1 and 65535.");
        }
    }
}
