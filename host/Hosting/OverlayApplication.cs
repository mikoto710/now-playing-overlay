using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Media.Spotify;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class OverlayApplication : IAsyncDisposable
{
    private readonly NowPlayingCoordinator _coordinator;
    private readonly HostRuntimeState _runtimeState;
    private readonly OverlayHttpServer _httpServer;
    private bool _started;
    private bool _disposed;

    private OverlayApplication(
        HostOptions options,
        HostStatusService statusService,
        NowPlayingCoordinator coordinator,
        HostRuntimeState runtimeState,
        OverlayHttpServer httpServer)
    {
        Options = options;
        StatusService = statusService;
        _coordinator = coordinator;
        _runtimeState = runtimeState;
        _httpServer = httpServer;
    }

    public HostOptions Options { get; }

    public int CurrentPort => _httpServer.CurrentPort;

    public HostStatusService StatusService { get; }

    public static OverlayApplication Build(
        string[] args,
        int? persistedPort = null,
        BoundedLogFile? applicationLog = null)
    {
        var options = HostOptionsLoader.Load(args, persistedPort);
        var loggerProvider = applicationLog is null
            ? null
            : new BoundedFileLoggerProvider(applicationLog);
        var sessionSource = new SpotifySessionMonitor(
            new WindowsMediaSessionManagerFactory(),
            new SpotifySessionMatcher(),
            CreateLogger<SpotifySessionMonitor>(loggerProvider));
        return Build(
            options,
            sessionSource,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
            loggerProvider);
    }

    internal static OverlayApplication Build(
        HostOptions options,
        ISessionSource sessionSource,
        OverlayPageAsset pageAsset)
    {
        return Build(options, sessionSource, pageAsset, loggerProvider: null);
    }

    private static OverlayApplication Build(
        HostOptions options,
        ISessionSource sessionSource,
        OverlayPageAsset pageAsset,
        BoundedFileLoggerProvider? loggerProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessionSource);
        ArgumentNullException.ThrowIfNull(pageAsset);
        options.Validate();

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
        var httpServer = new OverlayHttpServer(
            options,
            pageAsset,
            store,
            artworkCache,
            healthService,
            new SseConnectionLimiter(options.MaximumSseConnections),
            new RequestConnectionLimiter(options.MaximumConcurrentConnections),
            new ServerEndpointChangeBroadcaster(),
            CreateLogger<OverlayHttpServer>(loggerProvider));

        return new OverlayApplication(
            options,
            statusService,
            coordinator,
            runtimeState,
            httpServer);
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
