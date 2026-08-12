using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using OverlayHostOptions = NowPlayingOverlay.Host.Configuration.HostOptions;

namespace NowPlayingOverlay.Host.Shell;

internal sealed class TrayMenuController
{
    internal static IReadOnlyList<OverlayPreviewOption> OverlayPreviewOptions { get; } =
        Array.AsReadOnly(
            new OverlayPreviewOption[]
            {
                new(Scale: 1, Width: 350, Height: 70),
                new(Scale: 2, Width: 700, Height: 140),
                new(Scale: 3, Width: 1050, Height: 210),
                new(Scale: 4, Width: 1400, Height: 280),
                new(Scale: 5, Width: 1750, Height: 350),
            });

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

    public string BuildOverlayPreviewUrl(int previewScale)
    {
        return BuildOverlayPreviewUrl(EffectivePort, previewScale);
    }

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

    internal static string BuildOverlayPreviewUrl(int port, int previewScale)
    {
        if (previewScale is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(previewScale));
        }

        return $"{BuildOverlayUrl(port)}?previewScale={previewScale}";
    }
}

internal sealed record OverlayPreviewOption(int Scale, int Width, int Height)
{
    public string MenuText => $"{Width} x {Height}";
}

internal sealed record PortChangeResult(
    bool Changed,
    string OverlayUrl);
