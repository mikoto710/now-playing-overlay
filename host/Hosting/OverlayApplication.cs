using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.Spotify.Playback;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class OverlayApplication : IAsyncDisposable
{
    private readonly NowPlayingCoordinator _coordinator;
    private readonly ActiveSourceManager? _activeSourceManager;
    private readonly WindowsMediaSource? _windowsMediaSource;
    private readonly SpotifyApiSource? _spotifyApiSource;
    private readonly SpotifyAuthorizationService? _spotifyAuthorizationService;
    private readonly HostRuntimeState _runtimeState;
    private readonly AppearanceState _appearanceState;
    private readonly OverlayHttpServer _httpServer;
    private bool _started;
    private bool _disposed;

    private OverlayApplication(
        HostOptions options,
        HostStatusService statusService,
        NowPlayingCoordinator coordinator,
        ActiveSourceManager? activeSourceManager,
        WindowsMediaSource? windowsMediaSource,
        SpotifyApiSource? spotifyApiSource,
        SpotifyAuthorizationService? spotifyAuthorizationService,
        HostRuntimeState runtimeState,
        AppearanceState appearanceState,
        OverlayHttpServer httpServer)
    {
        Options = options;
        StatusService = statusService;
        _coordinator = coordinator;
        _activeSourceManager = activeSourceManager;
        _windowsMediaSource = windowsMediaSource;
        _spotifyApiSource = spotifyApiSource;
        _spotifyAuthorizationService = spotifyAuthorizationService;
        _runtimeState = runtimeState;
        _appearanceState = appearanceState;
        _httpServer = httpServer;
    }

    public HostOptions Options { get; }

    public int CurrentPort => _httpServer.CurrentPort;

    public HostStatusService StatusService { get; }

    public static OverlayApplication Build(
        string[] args,
        ApplicationSettings settings,
        ApplicationPaths paths,
        BoundedLogFile? applicationLog = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);
        settings.Validate();
        var options = HostOptionsLoader.Load(args, settings.Port);
        var loggerProvider = applicationLog is null
            ? null
            : new BoundedFileLoggerProvider(applicationLog);
        var windowsMediaSource = new WindowsMediaSource(
            new WindowsMediaSessionManagerFactory(),
            new WindowsMediaSessionMatcher(),
            CreateLogger<WindowsMediaSource>(loggerProvider));
        var spotifyCallbackBroker = new SpotifyAuthorizationCallbackBroker();
        var spotifyAuthorizationService = new SpotifyAuthorizationService(
            paths.SpotifyCredentialsFilePath,
            spotifyCallbackBroker);
        var spotifyApiSource = new SpotifyApiSource(
            new SpotifyCurrentlyPlayingClient(spotifyAuthorizationService),
            settings.Spotify.ToClientId(),
            logger: CreateLogger<SpotifyApiSource>(loggerProvider));
        var sessionSource = new ActiveSourceManager(
            [windowsMediaSource, spotifyApiSource],
            settings.Source.ToDescriptor());
        return Build(
            options,
            sessionSource,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
            loggerProvider,
            sessionSource,
            windowsMediaSource,
            spotifyApiSource,
            spotifyAuthorizationService,
            spotifyCallbackBroker,
            settings.Appearance);
    }

    internal static OverlayApplication Build(
        HostOptions options,
        ISessionSource sessionSource,
        OverlayPageAsset pageAsset,
        SpotifyAuthorizationCallbackBroker? spotifyCallbackBroker = null)
    {
        return Build(
            options,
            sessionSource,
            pageAsset,
            loggerProvider: null,
            activeSourceManager: null,
            windowsMediaSource: null,
            spotifyApiSource: null,
            spotifyAuthorizationService: null,
            spotifyCallbackBroker: spotifyCallbackBroker
                ?? new SpotifyAuthorizationCallbackBroker(),
            appearance: new AppearanceSettings());
    }

    private static OverlayApplication Build(
        HostOptions options,
        ISessionSource sessionSource,
        OverlayPageAsset pageAsset,
        BoundedFileLoggerProvider? loggerProvider,
        ActiveSourceManager? activeSourceManager,
        WindowsMediaSource? windowsMediaSource,
        SpotifyApiSource? spotifyApiSource,
        SpotifyAuthorizationService? spotifyAuthorizationService,
        SpotifyAuthorizationCallbackBroker spotifyCallbackBroker,
        AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessionSource);
        ArgumentNullException.ThrowIfNull(pageAsset);
        ArgumentNullException.ThrowIfNull(spotifyCallbackBroker);
        ArgumentNullException.ThrowIfNull(appearance);
        options.Validate();
        appearance.Validate();

        var timeProvider = TimeProvider.System;
        var runtimeState = new HostRuntimeState(timeProvider);
        var store = new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), timeProvider.GetUtcNow()),
            CreateLogger<NowPlayingStore>(loggerProvider));
        var artworkCache = new ArtworkCache();
        var coordinator = new NowPlayingCoordinator(
            sessionSource,
            store,
            artworkCache,
            timeProvider: timeProvider,
            logger: CreateLogger<NowPlayingCoordinator>(loggerProvider));
        var healthService = new HostHealthService(
            runtimeState,
            store,
            coordinator,
            sessionSource,
            timeProvider);
        var statusService = new HostStatusService(
            runtimeState,
            coordinator,
            sessionSource,
            store);
        var appearanceState = new AppearanceState(appearance);
        var httpServer = new OverlayHttpServer(
            options,
            pageAsset,
            store,
            artworkCache,
            healthService,
            appearanceState,
            new ConnectionLimiter(options.MaximumSseConnections),
            new ConnectionLimiter(options.MaximumConcurrentConnections),
            new ServerEndpointChangeBroadcaster(),
            spotifyCallbackBroker,
            CreateLogger<OverlayHttpServer>(loggerProvider));

        return new OverlayApplication(
            options,
            statusService,
            coordinator,
            activeSourceManager,
            windowsMediaSource,
            spotifyApiSource,
            spotifyAuthorizationService,
            runtimeState,
            appearanceState,
            httpServer);
    }

    public SourceManagerState GetSourceState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _activeSourceManager?.GetState() ?? SourceManagerState.Unconfigured;
    }

    public Task<SourceDiscoveryResult> RefreshWindowsMediaSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_windowsMediaSource is null)
        {
            throw new InvalidOperationException("This host does not have Windows Media discovery.");
        }

        return _windowsMediaSource.RefreshSourcesAsync(cancellationToken);
    }

    public void SelectSource(SourceSelectionSettings source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        if (_activeSourceManager is null)
        {
            throw new InvalidOperationException("This host does not have a configurable source manager.");
        }

        _activeSourceManager.Select(source.ToDescriptor());
    }

    public SpotifyConnectionState GetSpotifyConnectionState(SpotifyClientId clientId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetSpotifyAuthorizationService().GetConnectionState(clientId);
    }

    public Task<SpotifyConnectionState> ConnectSpotifyAsync(
        SpotifyClientId clientId,
        CancellationToken cancellationToken = default)
    {
        return AuthorizeSpotifyAsync(clientId, reauthorize: false, cancellationToken);
    }

    public Task<SpotifyConnectionState> ReauthorizeSpotifyAsync(
        SpotifyClientId clientId,
        CancellationToken cancellationToken = default)
    {
        return AuthorizeSpotifyAsync(clientId, reauthorize: true, cancellationToken);
    }

    public async Task DisconnectSpotifyAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await GetSpotifyAuthorizationService().DisconnectAsync(cancellationToken);
        _spotifyApiSource?.SetClientId(null);
    }

    public void SetAppearance(AppearanceSettings appearance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _appearanceState.Set(appearance);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("The overlay application has already started.");
        }

        await _httpServer.StartAsync(cancellationToken);
        try
        {
            _coordinator.Start();
            _runtimeState.MarkReady();
            _started = true;
        }
        catch
        {
            await _httpServer.StopAsync(cancellationToken);
            throw;
        }
    }

    public Task RebindPortAsync(
        int newPort,
        Action persistPort,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _httpServer.RebindAsync(newPort, persistPort, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        await _httpServer.StopAsync(cancellationToken);
        await _coordinator.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync();
        await _httpServer.DisposeAsync();
        _disposed = true;
    }

    private static ILogger<T> CreateLogger<T>(BoundedFileLoggerProvider? provider)
    {
        return provider?.CreateLogger<T>() ?? NullLogger<T>.Instance;
    }

    private async Task<SpotifyConnectionState> AuthorizeSpotifyAsync(
        SpotifyClientId clientId,
        bool reauthorize,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var service = GetSpotifyAuthorizationService();
        var redirectUri = SpotifyAuthorizationRequest.CreateLoopbackRedirectUri(CurrentPort);
        var state = reauthorize
            ? await service.ReauthorizeAsync(clientId, redirectUri, cancellationToken)
            : await service.ConnectAsync(clientId, redirectUri, cancellationToken);
        if (state.Status == SpotifyConnectionStatus.Connected)
        {
            _spotifyApiSource?.SetClientId(clientId);
        }

        return state;
    }

    private SpotifyAuthorizationService GetSpotifyAuthorizationService()
    {
        return _spotifyAuthorizationService
            ?? throw new InvalidOperationException("This host does not have Spotify authorization.");
    }
}
