namespace NowPlayingOverlay.Host.Configuration;

internal sealed record ApplicationSettings
{
    public int Port { get; init; } = HostOptions.DefaultPort;

    public void Validate()
    {
        if (Port is < 1 or > 65535)
        {
            throw new InvalidDataException("The configured port must be between 1 and 65535.");
        }
    }
}
