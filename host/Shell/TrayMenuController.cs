using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using OverlayHostOptions = NowPlayingOverlay.Host.Configuration.HostOptions;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class TrayMenuController
{
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly Func<HostStatus> _getStatus;
    private readonly Func<int> _getEffectivePort;
    private readonly Func<int, Action, CancellationToken, Task> _rebindPort;

    public TrayMenuController(
        Func<int> getEffectivePort,
        ApplicationSettingsStore settingsStore,
        HostStatusService statusService,
        string logDirectory,
        Func<int, Action, CancellationToken, Task> rebindPort)
        : this(
            getEffectivePort,
            settingsStore,
            statusService is null
                ? throw new ArgumentNullException(nameof(statusService))
                : statusService.GetCurrent,
            logDirectory,
            rebindPort)
    {
    }

    internal TrayMenuController(
        Func<int> getEffectivePort,
        ApplicationSettingsStore settingsStore,
        Func<HostStatus> getStatus,
        string logDirectory,
        Func<int, Action, CancellationToken, Task> rebindPort)
    {
        _getEffectivePort = getEffectivePort ?? throw new ArgumentNullException(nameof(getEffectivePort));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _getStatus = getStatus ?? throw new ArgumentNullException(nameof(getStatus));
        _rebindPort = rebindPort ?? throw new ArgumentNullException(nameof(rebindPort));
        LogDirectory = Path.GetFullPath(
            logDirectory ?? throw new ArgumentNullException(nameof(logDirectory)));
    }

    public int EffectivePort => _getEffectivePort();

    public string OverlayUrl => BuildOverlayUrl(EffectivePort);

    public string LogDirectory { get; }

    public HostStatus GetStatus()
    {
        return _getStatus();
    }

    public async Task<PortChangeResult> SavePortAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        var settings = new ApplicationSettings { Port = port };
        settings.Validate();
        if (port == EffectivePort)
        {
            return new PortChangeResult(Changed: false, OverlayUrl);
        }

        await _rebindPort(
            port,
            () => _settingsStore.Save(settings),
            cancellationToken);
        return new PortChangeResult(
            Changed: true,
            OverlayUrl);
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
    string OverlayUrl);
