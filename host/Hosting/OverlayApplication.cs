using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.Spotify.Playback;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Outputs;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class OverlayApplication : IAsyncDisposable
{
    private readonly NowPlayingCoordinator _coordinator;
    private readonly ActiveSourceManager? _activeSourceManager;
    private readonly WindowsMediaSource? _windowsMediaSource;
    private readonly SpotifyApiSource? _spotifyApiSource;
    private readonly ExternalPushSource? _externalPushSource;
    private readonly SpotifyAuthorizationService? _spotifyAuthorizationService;
    private readonly IngestKeyStore? _ingestKeyStore;
    private readonly ExternalIngestHttpHandler? _externalIngestHandler;
    private readonly HostRuntimeState _runtimeState;
    private readonly AppearanceState _appearanceState;
    private readonly OverlayHttpServer _httpServer;
    private readonly OutputManager _outputManager;
    private bool _started;
    private bool _disposed;

    private OverlayApplication(
        HostOptions options,
        HostStatusService statusService,
        NowPlayingCoordinator coordinator,
        ActiveSourceManager? activeSourceManager,
        WindowsMediaSource? windowsMediaSource,
        SpotifyApiSource? spotifyApiSource,
        ExternalPushSource? externalPushSource,
        SpotifyAuthorizationService? spotifyAuthorizationService,
        IngestKeyStore? ingestKeyStore,
        ExternalIngestHttpHandler? externalIngestHandler,
        HostRuntimeState runtimeState,
        AppearanceState appearanceState,
        OutputManager outputManager,
        OverlayHttpServer httpServer)
    {
        Options = options;
        StatusService = statusService;
        _coordinator = coordinator;
        _activeSourceManager = activeSourceManager;
        _windowsMediaSource = windowsMediaSource;
        _spotifyApiSource = spotifyApiSource;
        _externalPushSource = externalPushSource;
        _spotifyAuthorizationService = spotifyAuthorizationService;
        _ingestKeyStore = ingestKeyStore;
        _externalIngestHandler = externalIngestHandler;
        _runtimeState = runtimeState;
        _appearanceState = appearanceState;
        _outputManager = outputManager;
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
        var externalLease = new ExternalProducerLease(ExternalPushSource.DefaultLeaseDuration);
        var externalPushSource = new ExternalPushSource(externalLease);
        var ingestKeyStore = new IngestKeyStore(paths.IngestKeyFilePath);
        var externalIngestHandler = new ExternalIngestHttpHandler(
            ingestKeyStore.LoadOrCreate(),
            externalLease);
        var sessionSource = new ActiveSourceManager(
            [windowsMediaSource, spotifyApiSource, externalPushSource],
            settings.Source.ToDescriptor());
        return Build(
            options,
            sessionSource,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
            loggerProvider,
            sessionSource,
            windowsMediaSource,
            spotifyApiSource,
            externalPushSource,
            spotifyAuthorizationService,
            ingestKeyStore,
            spotifyCallbackBroker,
            externalIngestHandler,
            appearance: settings.Appearance,
            outputs: settings.Outputs);
    }

    internal static OverlayApplication Build(
        HostOptions options,
        ISessionSource sessionSource,
        OverlayPageAsset pageAsset,
        SpotifyAuthorizationCallbackBroker? spotifyCallbackBroker = null,
        ExternalIngestHttpHandler? externalIngestHandler = null)
    {
        return Build(
            options,
            sessionSource,
            pageAsset,
            loggerProvider: null,
            activeSourceManager: null,
            windowsMediaSource: null,
            spotifyApiSource: null,
            externalPushSource: null,
            spotifyAuthorizationService: null,
            ingestKeyStore: null,
            spotifyCallbackBroker: spotifyCallbackBroker
                ?? new SpotifyAuthorizationCallbackBroker(),
            externalIngestHandler: externalIngestHandler,
            appearance: new AppearanceSettings(),
            outputs: new OutputSettings());
    }

    private static OverlayApplication Build(
        HostOptions options,
        ISessionSource sessionSource,
        OverlayPageAsset pageAsset,
        BoundedFileLoggerProvider? loggerProvider,
        ActiveSourceManager? activeSourceManager,
        WindowsMediaSource? windowsMediaSource,
        SpotifyApiSource? spotifyApiSource,
        ExternalPushSource? externalPushSource,
        SpotifyAuthorizationService? spotifyAuthorizationService,
        IngestKeyStore? ingestKeyStore,
        SpotifyAuthorizationCallbackBroker spotifyCallbackBroker,
        ExternalIngestHttpHandler? externalIngestHandler,
        AppearanceSettings appearance,
        OutputSettings outputs)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessionSource);
        ArgumentNullException.ThrowIfNull(pageAsset);
        ArgumentNullException.ThrowIfNull(spotifyCallbackBroker);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(outputs);
        options.Validate();
        appearance.Validate();
        outputs.Validate();

        var timeProvider = TimeProvider.System;
        var runtimeState = new HostRuntimeState(timeProvider);
        var store = new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), timeProvider.GetUtcNow()),
            CreateLogger<NowPlayingStore>(loggerProvider));
        var artworkCache = new ArtworkCache();
        var outputManager = new OutputManager(
            store,
            artworkCache,
            outputs,
            logger: CreateLogger<OutputManager>(loggerProvider));
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
            externalIngestHandler,
            CreateLogger<OverlayHttpServer>(loggerProvider));

        return new OverlayApplication(
            options,
            statusService,
            coordinator,
            activeSourceManager,
            windowsMediaSource,
            spotifyApiSource,
            externalPushSource,
            spotifyAuthorizationService,
            ingestKeyStore,
            externalIngestHandler,
            runtimeState,
            appearanceState,
            outputManager,
            httpServer);
    }

    public SourceManagerState GetSourceState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _activeSourceManager?.GetState() ?? SourceManagerState.Unconfigured;
    }

    public Task<SourceDiscoveryResult> RefreshSourcesAsync(
        SourceProvider provider,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return provider switch
        {
            SourceProvider.WindowsMedia when _windowsMediaSource is not null =>
                _windowsMediaSource.RefreshSourcesAsync(cancellationToken),
            SourceProvider.SpotifyApi when _spotifyApiSource is not null =>
                Task.FromResult(new SourceDiscoveryResult(
                    [SourceDescriptor.SpotifyApi()],
                    GetSourceState())),
            SourceProvider.ExternalPush when _externalPushSource is not null =>
                Task.FromResult(new SourceDiscoveryResult(
                    [SourceDescriptor.ExternalPush()],
                    GetSourceState())),
            _ => throw new InvalidOperationException(
                "This host does not have the requested source provider."),
        };
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

    public void SetOutputs(OutputSettings outputs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _outputManager.UpdateSettings(outputs);
    }

    public OutputStatusSnapshot GetOutputStatus()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _outputManager.GetStatus();
    }

    public string RenderOutputPreview(string template)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _outputManager.RenderPreview(template);
    }

    public string ExportIngestKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetExternalIngestHandler().ExportKey();
    }

    public string RotateIngestKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var store = _ingestKeyStore
            ?? throw new InvalidOperationException("This host does not have an ingest key store.");
        var replacement = store.Rotate();
        var transferred = false;
        try
        {
            GetExternalIngestHandler().ReplaceKey(replacement);
            transferred = true;
            return GetExternalIngestHandler().ExportKey();
        }
        finally
        {
            if (!transferred)
            {
                replacement.Dispose();
            }
        }
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
            _outputManager.Start();
            _coordinator.Start();
            _runtimeState.MarkReady();
            _started = true;
        }
        catch
        {
            await _outputManager.StopAsync();
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
        await _outputManager.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync();
        await _httpServer.DisposeAsync();
        await _outputManager.DisposeAsync();
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

    private ExternalIngestHttpHandler GetExternalIngestHandler()
    {
        return _externalIngestHandler
            ?? throw new InvalidOperationException("This host does not have external ingest enabled.");
    }
}
