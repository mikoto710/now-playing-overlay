using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media;
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
    private readonly Func<SourceManagerState> _getSourceState;
    private readonly Func<CancellationToken, Task<SourceDiscoveryResult>> _refreshSources;
    private readonly Action<string?> _selectWindowsMedia;

    public TrayMenuController(
        Func<int> getEffectivePort,
        ApplicationSettingsStore settingsStore,
        HostStatusService statusService,
        string logDirectory,
        Func<int, Action, CancellationToken, Task> rebindPort,
        Func<SourceManagerState> getSourceState,
        Func<CancellationToken, Task<SourceDiscoveryResult>> refreshSources,
        Action<string?> selectWindowsMedia)
        : this(
            getEffectivePort,
            settingsStore,
            statusService is null
                ? throw new ArgumentNullException(nameof(statusService))
                : statusService.GetCurrent,
            logDirectory,
            rebindPort,
            getSourceState,
            refreshSources,
            selectWindowsMedia)
    {
    }

    internal TrayMenuController(
        Func<int> getEffectivePort,
        ApplicationSettingsStore settingsStore,
        Func<HostStatus> getStatus,
        string logDirectory,
        Func<int, Action, CancellationToken, Task> rebindPort,
        Func<SourceManagerState>? getSourceState = null,
        Func<CancellationToken, Task<SourceDiscoveryResult>>? refreshSources = null,
        Action<string?>? selectWindowsMedia = null)
    {
        _getEffectivePort = getEffectivePort ?? throw new ArgumentNullException(nameof(getEffectivePort));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _getStatus = getStatus ?? throw new ArgumentNullException(nameof(getStatus));
        _rebindPort = rebindPort ?? throw new ArgumentNullException(nameof(rebindPort));
        _getSourceState = getSourceState ?? (() => SourceManagerState.Unconfigured);
        _refreshSources = refreshSources ?? (_ => Task.FromResult(
            new SourceDiscoveryResult([], _getSourceState())));
        _selectWindowsMedia = selectWindowsMedia ?? (_ => { });
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

    public SourceManagerState GetSourceState()
    {
        return _getSourceState();
    }

    public Task<SourceDiscoveryResult> RefreshSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        return _refreshSources(cancellationToken);
    }

    public async Task<PortChangeResult> SavePortAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidDataException("The configured port must be between 1 and 65535.");
        }

        if (port == EffectivePort)
        {
            return new PortChangeResult(Changed: false, OverlayUrl);
        }

        await _rebindPort(
            port,
            () => _settingsStore.Update(current => current with { Port = port }),
            cancellationToken);
        return new PortChangeResult(
            Changed: true,
            OverlayUrl);
    }

    public void SaveSource(string? sourceAppUserModelId)
    {
        var source = new SourceSelectionSettings
        {
            Provider = SourceProvider.WindowsMedia,
            SourceAppUserModelId = sourceAppUserModelId,
        };
        source.Validate();
        _settingsStore.Update(current => current with { Source = source });
        _selectWindowsMedia(sourceAppUserModelId);
    }

    public async Task<SettingsChangeResult> SaveSettingsAsync(
        int port,
        string? sourceAppUserModelId,
        CancellationToken cancellationToken = default)
    {
        var source = new SourceSelectionSettings
        {
            Provider = SourceProvider.WindowsMedia,
            SourceAppUserModelId = sourceAppUserModelId,
        };
        var settings = new ApplicationSettings
        {
            Port = port,
            Source = source,
        };
        settings.Validate();

        var portChanged = port != EffectivePort;
        if (portChanged)
        {
            await _rebindPort(
                port,
                () => _settingsStore.Update(current => current with
                {
                    Port = port,
                    Source = source,
                }),
                cancellationToken);
        }
        else
        {
            _settingsStore.Update(current => current with
            {
                Port = port,
                Source = source,
            });
        }

        var sourceChanged = !string.Equals(
            GetSourceState().ActiveSource?.Key.InstanceId,
            sourceAppUserModelId,
            StringComparison.Ordinal);
        if (sourceChanged)
        {
            _selectWindowsMedia(sourceAppUserModelId);
        }

        return new SettingsChangeResult(portChanged, sourceChanged, OverlayUrl);
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

internal sealed record SettingsChangeResult(
    bool PortChanged,
    bool SourceChanged,
    string OverlayUrl);
