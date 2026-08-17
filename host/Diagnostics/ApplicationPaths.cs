namespace NowPlayingOverlay.Host.Diagnostics;

internal sealed record ApplicationPaths
{
    private const string ApplicationDirectoryName = "NowPlayingOverlay";

    public ApplicationPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        LogDirectory = Path.Combine(RootDirectory, "logs");
        LogFilePath = Path.Combine(LogDirectory, "NowPlayingOverlay.log");
        SettingsFilePath = Path.Combine(RootDirectory, "settings.json");
        SpotifyCredentialsFilePath = Path.Combine(RootDirectory, "spotify-credentials.dat");
    }

    public string RootDirectory { get; }

    public string LogDirectory { get; }

    public string LogFilePath { get; }

    public string SettingsFilePath { get; }

    public string SpotifyCredentialsFilePath { get; }

    public static ApplicationPaths ForCurrentUser()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The current user's local application data directory is unavailable.");
        }

        return new ApplicationPaths(Path.Combine(localAppData, ApplicationDirectoryName));
    }
}
