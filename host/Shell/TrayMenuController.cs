using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Shell;

using OverlayHostOptions = Configuration.HostOptions;

internal sealed class TrayMenuController
{
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly Func<TrayStatus> _getStatus;
    private readonly Func<int, bool> _isPortAvailable;

    public TrayMenuController(
        OverlayHostOptions hostOptions,
        ApplicationSettingsStore settingsStore,
        TrayStatusService statusService,
        string logDirectory)
        : this(
            hostOptions,
            settingsStore,
            statusService is null
                ? throw new ArgumentNullException(nameof(statusService))
                : statusService.GetCurrent,
            logDirectory,
            LoopbackPortProbe.IsAvailable)
    {
    }

    internal TrayMenuController(
        OverlayHostOptions hostOptions,
        ApplicationSettingsStore settingsStore,
        Func<TrayStatus> getStatus,
        string logDirectory,
        Func<int, bool>? isPortAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        EffectivePort = hostOptions.Port;
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _getStatus = getStatus ?? throw new ArgumentNullException(nameof(getStatus));
        _isPortAvailable = isPortAvailable ?? LoopbackPortProbe.IsAvailable;
        LogDirectory = Path.GetFullPath(
            logDirectory ?? throw new ArgumentNullException(nameof(logDirectory)));
    }

    public int EffectivePort { get; }

    public string OverlayUrl => BuildOverlayUrl(EffectivePort);

    public string LogDirectory { get; }

    public TrayStatus GetStatus()
    {
        return _getStatus();
    }

    public PortChangeResult SavePort(int port)
    {
        var settings = new ApplicationSettings { Port = port };
        settings.Validate();
        if (port == EffectivePort)
        {
            return new PortChangeResult(Changed: false, RequiresRestart: false, OverlayUrl);
        }

        if (!_isPortAvailable(port))
        {
            throw new InvalidOperationException($"Port {port} is not available on 127.0.0.1.");
        }

        _settingsStore.Save(settings);
        return new PortChangeResult(
            Changed: true,
            RequiresRestart: true,
            BuildOverlayUrl(port));
    }

    internal static string BuildOverlayUrl(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        return $"http://{OverlayHostOptions.AllowedHost}:{port}/NowPlaying.html";
    }
}

internal sealed record PortChangeResult(
    bool Changed,
    bool RequiresRestart,
    string OverlayUrl);
