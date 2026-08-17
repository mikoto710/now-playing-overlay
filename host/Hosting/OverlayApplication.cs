using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class OverlayApplication : IAsyncDisposable
{
    private readonly NowPlayingCoordinator _coordinator;
    private readonly ActiveSourceManager? _activeSourceManager;
    private readonly WindowsMediaSource? _windowsMediaSource;
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
        HostRuntimeState runtimeState,
        AppearanceState appearanceState,
        OverlayHttpServer httpServer)
    {
        Options = options;
        StatusService = statusService;
        _coordinator = coordinator;
        _activeSourceManager = activeSourceManager;
        _windowsMediaSource = windowsMediaSource;
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
        BoundedLogFile? applicationLog = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var options = HostOptionsLoader.Load(args, settings.Port);
        var loggerProvider = applicationLog is null
            ? null
            : new BoundedFileLoggerProvider(applicationLog);
        var windowsMediaSource = new WindowsMediaSource(
            new WindowsMediaSessionManagerFactory(),
            new WindowsMediaSessionMatcher(),
            CreateLogger<WindowsMediaSource>(loggerProvider));
        var sessionSource = new ActiveSourceManager(
            [windowsMediaSource],
            settings.Source.ToDescriptor());
        return Build(
            options,
            sessionSource,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
            loggerProvider,
            sessionSource,
            windowsMediaSource,
            settings.Appearance);
    }

    internal static OverlayApplication Build(
        HostOptions options,
        ISessionSource sessionSource,
        OverlayPageAsset pageAsset)
    {
        return Build(
            options,
            sessionSource,
            pageAsset,
            loggerProvider: null,
            activeSourceManager: null,
            windowsMediaSource: null,
            appearance: new AppearanceSettings());
    }

    private static OverlayApplication Build(
        HostOptions options,
        ISessionSource sessionSource,
        OverlayPageAsset pageAsset,
        BoundedFileLoggerProvider? loggerProvider,
        ActiveSourceManager? activeSourceManager,
        WindowsMediaSource? windowsMediaSource,
        AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessionSource);
        ArgumentNullException.ThrowIfNull(pageAsset);
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
            CreateLogger<OverlayHttpServer>(loggerProvider));

        return new OverlayApplication(
            options,
            statusService,
            coordinator,
            activeSourceManager,
            windowsMediaSource,
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

    public void SelectWindowsMedia(string? sourceAppUserModelId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeSourceManager is null)
        {
            throw new InvalidOperationException("This host does not have a configurable source manager.");
        }

        _activeSourceManager.Select(
            string.IsNullOrWhiteSpace(sourceAppUserModelId)
                ? null
                : SourceDescriptor.WindowsMedia(sourceAppUserModelId));
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
}
