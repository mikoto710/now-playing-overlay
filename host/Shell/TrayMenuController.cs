using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.WindowTitles;
using NowPlayingOverlay.Host.Outputs;

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
    private readonly Func<SourceProvider, CancellationToken, Task<SourceDiscoveryResult>>
        _refreshSources;
    private readonly Action<SourceSelectionSettings> _selectSource;
    private readonly Func<SpotifyClientId, SpotifyConnectionState> _getSpotifyConnectionState;
    private readonly Func<SpotifyClientId, bool, CancellationToken, Task<SpotifyConnectionState>>
        _authorizeSpotify;
    private readonly Func<CancellationToken, Task> _disconnectSpotify;
    private readonly Func<string> _exportIngestKey;
    private readonly Func<string> _rotateIngestKey;
    private readonly Action<AppearanceSettings> _setAppearance;
    private readonly Func<OutputStatusSnapshot> _getOutputStatus;
    private readonly Func<string, string> _renderOutputPreview;
    private readonly Action<OutputSettings> _setOutputs;
    private readonly Func<CancellationToken, Task<WindowTitleDiscoveryResult>> _refreshWindowTitles;
    private readonly Action<WindowTitleSettings> _setWindowTitleSettings;

    public TrayMenuController(
        Func<int> getEffectivePort,
        ApplicationSettingsStore settingsStore,
        HostStatusService statusService,
        string logDirectory,
        Func<int, Action, CancellationToken, Task> rebindPort,
        Func<SourceManagerState> getSourceState,
        Func<SourceProvider, CancellationToken, Task<SourceDiscoveryResult>> refreshSources,
        Action<SourceSelectionSettings> selectSource,
        Func<SpotifyClientId, SpotifyConnectionState> getSpotifyConnectionState,
        Func<SpotifyClientId, bool, CancellationToken, Task<SpotifyConnectionState>> authorizeSpotify,
        Func<CancellationToken, Task> disconnectSpotify,
        Func<string> exportIngestKey,
        Func<string> rotateIngestKey,
        Action<AppearanceSettings> setAppearance,
        Func<OutputStatusSnapshot> getOutputStatus,
        Func<string, string> renderOutputPreview,
        Action<OutputSettings> setOutputs,
        Func<CancellationToken, Task<WindowTitleDiscoveryResult>> refreshWindowTitles,
        Action<WindowTitleSettings> setWindowTitleSettings)
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
            selectSource,
            getSpotifyConnectionState,
            authorizeSpotify,
            disconnectSpotify,
            exportIngestKey,
            rotateIngestKey,
            setAppearance,
            getOutputStatus,
            renderOutputPreview,
            setOutputs,
            refreshWindowTitles,
            setWindowTitleSettings)
    {
    }

    internal TrayMenuController(
        Func<int> getEffectivePort,
        ApplicationSettingsStore settingsStore,
        Func<HostStatus> getStatus,
        string logDirectory,
        Func<int, Action, CancellationToken, Task> rebindPort,
        Func<SourceManagerState>? getSourceState = null,
        Func<SourceProvider, CancellationToken, Task<SourceDiscoveryResult>>? refreshSources = null,
        Action<SourceSelectionSettings>? selectSource = null,
        Func<SpotifyClientId, SpotifyConnectionState>? getSpotifyConnectionState = null,
        Func<SpotifyClientId, bool, CancellationToken, Task<SpotifyConnectionState>>?
            authorizeSpotify = null,
        Func<CancellationToken, Task>? disconnectSpotify = null,
        Func<string>? exportIngestKey = null,
        Func<string>? rotateIngestKey = null,
        Action<AppearanceSettings>? setAppearance = null,
        Func<OutputStatusSnapshot>? getOutputStatus = null,
        Func<string, string>? renderOutputPreview = null,
        Action<OutputSettings>? setOutputs = null,
        Func<CancellationToken, Task<WindowTitleDiscoveryResult>>? refreshWindowTitles = null,
        Action<WindowTitleSettings>? setWindowTitleSettings = null)
    {
        _getEffectivePort = getEffectivePort ?? throw new ArgumentNullException(nameof(getEffectivePort));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _getStatus = getStatus ?? throw new ArgumentNullException(nameof(getStatus));
        _rebindPort = rebindPort ?? throw new ArgumentNullException(nameof(rebindPort));
        _getSourceState = getSourceState ?? (() => SourceManagerState.Unconfigured);
        _refreshSources = refreshSources ?? ((_, _) => Task.FromResult(
            new SourceDiscoveryResult([], _getSourceState())));
        _selectSource = selectSource ?? (_ => { });
        _getSpotifyConnectionState = getSpotifyConnectionState
            ?? (_ => new SpotifyConnectionState(SpotifyConnectionStatus.Disconnected));
        _authorizeSpotify = authorizeSpotify
            ?? ((_, _, _) => Task.FromResult(
                new SpotifyConnectionState(SpotifyConnectionStatus.Disconnected)));
        _disconnectSpotify = disconnectSpotify ?? (_ => Task.CompletedTask);
        _exportIngestKey = exportIngestKey ?? (() => new string('A', IngestKey.EncodedLength));
        _rotateIngestKey = rotateIngestKey ?? _exportIngestKey;
        _setAppearance = setAppearance ?? (_ => { });
        _getOutputStatus = getOutputStatus
            ?? (() => new OutputStatusSnapshot(0, "Outputs are ready. No output errors are recorded."));
        _renderOutputPreview = renderOutputPreview ?? (_ => string.Empty);
        _setOutputs = setOutputs ?? (_ => { });
        _refreshWindowTitles = refreshWindowTitles ?? (_ => Task.FromResult(
            new WindowTitleDiscoveryResult([], SourceManagerState.Unconfigured)));
        _setWindowTitleSettings = setWindowTitleSettings ?? (_ => { });
        LogDirectory = Path.GetFullPath(
            logDirectory ?? throw new ArgumentNullException(nameof(logDirectory)));
    }

    public int EffectivePort => _getEffectivePort();

    public string OverlayUrl => OverlayEndpoint.BuildUrl(EffectivePort);

    public string BrowserProducerUrl =>
        $"http://{HostOptions.AllowedHost}:{EffectivePort}{BrowserProducerAsset.Path}";

    public string BuildOverlayPreviewUrl(int previewScale)
    {
        return OverlayEndpoint.BuildPreviewUrl(EffectivePort, previewScale);
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
        SourceProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        return _refreshSources(provider, cancellationToken);
    }

    public ApplicationSettings GetSettings()
    {
        return _settingsStore.Load().Settings;
    }

    public Task<WindowTitleDiscoveryResult> RefreshWindowTitlesAsync(
        CancellationToken cancellationToken = default)
    {
        return _refreshWindowTitles(cancellationToken);
    }

    public OutputStatusSnapshot GetOutputStatus()
    {
        return _getOutputStatus();
    }

    public string RenderOutputPreview(string template)
    {
        return _renderOutputPreview(template);
    }

    public SpotifyConnectionSnapshot GetSpotifyConnection()
    {
        var clientId = _settingsStore.Load().Settings.Spotify.ClientId;
        if (clientId is null)
        {
            return SpotifyConnectionSnapshot.Disconnected;
        }

        var typedClientId = new SpotifyClientId(clientId);
        return new SpotifyConnectionSnapshot(clientId, _getSpotifyConnectionState(typedClientId));
    }

    public string GetBrowserPlayerConnectionCode()
    {
        return ExternalIngestConnectionCode.Create(EffectivePort, _exportIngestKey());
    }

    public string RotateBrowserPlayerConnectionCode()
    {
        return ExternalIngestConnectionCode.Create(EffectivePort, _rotateIngestKey());
    }

    public async Task<SpotifyConnectionSnapshot> AuthorizeSpotifyAsync(
        string clientId,
        bool reauthorize,
        CancellationToken cancellationToken = default)
    {
        var typedClientId = new SpotifyClientId(clientId);
        var state = await _authorizeSpotify(
            typedClientId,
            reauthorize,
            cancellationToken);
        if (state.Status != SpotifyConnectionStatus.Connected)
        {
            return new SpotifyConnectionSnapshot(typedClientId.Value, state);
        }

        _settingsStore.Update(current => current with
        {
            Spotify = new SpotifyConnectionSettings { ClientId = typedClientId.Value },
        });
        return new SpotifyConnectionSnapshot(typedClientId.Value, state);
    }

    public async Task<SpotifyConnectionSnapshot> DisconnectSpotifyAsync(
        CancellationToken cancellationToken = default)
    {
        await _disconnectSpotify(cancellationToken);
        SourceSelectionSettings? fallback = null;
        _settingsStore.Update(current =>
        {
            var source = current.Source;
            if (source.Provider == SourceProvider.SpotifyApi)
            {
                source = SourceSelectionSettings.WindowsMedia(
                    current.WindowsMedia.LastInstanceId);
                fallback = source;
            }

            return current with
            {
                Source = source,
                Spotify = new SpotifyConnectionSettings(),
            };
        });
        if (fallback is not null)
        {
            _selectSource(fallback);
        }

        return SpotifyConnectionSnapshot.Disconnected;
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

    public async Task<SettingsChangeResult> SaveSettingsAsync(
        int port,
        SourceProvider provider,
        string? instanceId,
        AppearanceSettings appearance,
        OutputSettings? outputs = null,
        WindowTitleSettings? windowTitle = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        var source = new SourceSelectionSettings
        {
            Provider = provider,
            InstanceId = instanceId,
        };
        var currentSettings = _settingsStore.Load().Settings;
        outputs ??= currentSettings.Outputs;
        windowTitle ??= currentSettings.WindowTitle;
        var windowsMedia = provider == SourceProvider.WindowsMedia
            ? currentSettings.WindowsMedia with { LastInstanceId = instanceId }
            : currentSettings.WindowsMedia;
        var settings = currentSettings with
        {
            Port = port,
            Source = source,
            WindowsMedia = windowsMedia,
            Appearance = appearance,
            Outputs = outputs,
            WindowTitle = windowTitle,
        };
        settings.Validate();
        if (provider == SourceProvider.SpotifyApi)
        {
            var clientId = settings.Spotify.ToClientId()
                ?? throw new InvalidDataException("Connect Spotify before selecting Spotify API.");
            if (_getSpotifyConnectionState(clientId).Status != SpotifyConnectionStatus.Connected)
            {
                throw new InvalidDataException("Reconnect Spotify before selecting Spotify API.");
            }
        }

        var portChanged = port != EffectivePort;
        if (portChanged)
        {
            await _rebindPort(
                port,
                () => _settingsStore.Update(current => current with
                {
                    Port = port,
                    Source = source,
                    WindowsMedia = windowsMedia,
                    Appearance = appearance,
                    Outputs = outputs,
                    WindowTitle = windowTitle,
                }),
                cancellationToken);
        }
        else
        {
            _settingsStore.Update(current => current with
            {
                Port = port,
                Source = source,
                WindowsMedia = windowsMedia,
                Appearance = appearance,
                Outputs = outputs,
                WindowTitle = windowTitle,
            });
        }

        _setWindowTitleSettings(windowTitle);
        var selectedDescriptor = source.ToDescriptor();
        var sourceChanged = !Equals(
            GetSourceState().ActiveSource?.Key,
            selectedDescriptor?.Key);
        if (sourceChanged)
        {
            _selectSource(source);
        }

        _setAppearance(appearance);
        _setOutputs(outputs);

        return new SettingsChangeResult(portChanged, OverlayUrl);
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
    string OverlayUrl);

internal sealed record SpotifyConnectionSnapshot(
    string? ClientId,
    SpotifyConnectionState State)
{
    public static SpotifyConnectionSnapshot Disconnected { get; } = new(
        null,
        new SpotifyConnectionState(SpotifyConnectionStatus.Disconnected));
}
