using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.ControlPlane;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.WindowTitles;
using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.Shell;

/// <summary>
/// Adapts typed application workflows to the WinForms shell. It owns no persistence,
/// source-transition, authorization, or runtime-configuration policy.
/// </summary>
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

    private readonly OverlayRuntime _runtime;
    private readonly SettingsApplicationWorkflow _settings;
    private readonly MediaSourceService _sources;
    private readonly SpotifyConnectionWorkflow _spotify;
    private readonly BrowserPlayerConnectionService _browserPlayer;
    private readonly OutputManager _outputs;

    public TrayMenuController(
        OverlayRuntime runtime,
        SettingsApplicationWorkflow settings,
        MediaSourceService sources,
        SpotifyConnectionWorkflow spotify,
        BrowserPlayerConnectionService browserPlayer,
        OutputManager outputs,
        string logDirectory)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _spotify = spotify ?? throw new ArgumentNullException(nameof(spotify));
        _browserPlayer = browserPlayer ?? throw new ArgumentNullException(nameof(browserPlayer));
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        LogDirectory = Path.GetFullPath(
            logDirectory ?? throw new ArgumentNullException(nameof(logDirectory)));
    }

    public int EffectivePort => _runtime.CurrentPort;

    public string OverlayUrl => OverlayEndpoint.BuildUrl(EffectivePort);

    public string BrowserProducerUrl =>
        $"http://{HostOptions.AllowedHost}:{EffectivePort}{BrowserProducerAsset.Path}";

    public string LogDirectory { get; }

    public string BuildOverlayPreviewUrl(int previewScale)
    {
        return OverlayEndpoint.BuildPreviewUrl(EffectivePort, previewScale);
    }

    public HostStatus GetStatus()
    {
        return _runtime.StatusService.GetCurrent();
    }

    public SourceManagerState GetSourceState()
    {
        return _sources.GetState();
    }

    public Task<SourceDiscoveryResult> RefreshSourcesAsync(
        SourceProvider provider,
        CancellationToken cancellationToken = default)
    {
        return _sources.RefreshAsync(provider, cancellationToken);
    }

    public ApplicationSettings GetSettings()
    {
        return _settings.GetCurrent();
    }

    public Task<WindowTitleDiscoveryResult> RefreshWindowTitlesAsync(
        CancellationToken cancellationToken = default)
    {
        return _sources.RefreshWindowTitlesAsync(cancellationToken);
    }

    public OutputStatusSnapshot GetOutputStatus()
    {
        return _outputs.GetStatus();
    }

    public string RenderOutputPreview(string template)
    {
        return _outputs.RenderPreview(template);
    }

    public SpotifyConnectionSnapshot GetSpotifyConnection()
    {
        var clientId = _spotify.GetSavedClientId();
        if (clientId is null)
        {
            return SpotifyConnectionSnapshot.Disconnected;
        }

        var typedClientId = new SpotifyClientId(clientId);
        return new SpotifyConnectionSnapshot(
            clientId,
            _spotify.GetConnectionState(typedClientId));
    }

    public string GetBrowserPlayerConnectionCode()
    {
        return _browserPlayer.GetConnectionCode();
    }

    public string RotateBrowserPlayerConnectionCode()
    {
        return _browserPlayer.RotateConnectionCode();
    }

    public async Task<SpotifyConnectionSnapshot> AuthorizeSpotifyAsync(
        string clientId,
        bool reauthorize,
        CancellationToken cancellationToken = default)
    {
        var typedClientId = new SpotifyClientId(clientId);
        var state = await _spotify.AuthorizeAsync(
            typedClientId,
            reauthorize,
            cancellationToken);
        return new SpotifyConnectionSnapshot(typedClientId.Value, state);
    }

    public async Task<SpotifyConnectionSnapshot> DisconnectSpotifyAsync(
        CancellationToken cancellationToken = default)
    {
        await _spotify.DisconnectAsync(cancellationToken);
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

        var current = _settings.GetCurrent();
        var result = await _settings.ApplyAsync(
            new SettingsDraft(
                port,
                current.Source.Provider,
                current.Source.InstanceId,
                current.Appearance,
                current.Outputs,
                current.WindowTitle),
            cancellationToken);
        return new PortChangeResult(result.PortChanged, result.OverlayUrl);
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
        var current = _settings.GetCurrent();
        var result = await _settings.ApplyAsync(
            new SettingsDraft(
                port,
                provider,
                instanceId,
                appearance,
                outputs ?? current.Outputs,
                windowTitle ?? current.WindowTitle),
            cancellationToken);
        return new SettingsChangeResult(result.PortChanged, result.OverlayUrl);
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
