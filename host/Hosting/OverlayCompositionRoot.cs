using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.ControlPlane;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.Spotify.Playback;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Media.WindowTitles;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Outputs;
using NowPlayingOverlay.Host.Shell;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed record OverlayComposition(
    OverlayRuntime Runtime,
    TrayMenuController TrayController,
    SettingsApplicationWorkflow Settings,
    MediaSourceService Sources,
    SpotifyConnectionWorkflow Spotify,
    BrowserPlayerConnectionService BrowserPlayer);

internal sealed record OverlayRuntimeGraph(
    OverlayRuntime Runtime,
    OverlayHttpServer HttpServer,
    AppearanceState Appearance);

/// <summary>
/// Constructs the complete production object graph with explicit ownership. It performs no runtime
/// start/stop work; <see cref="OverlayRuntime"/> owns that one-shot lifecycle.
/// </summary>
internal static class OverlayCompositionRoot
{
    public static OverlayComposition Compose(
        string[] args,
        ApplicationSettings settings,
        ApplicationSettingsStore settingsStore,
        ApplicationPaths paths,
        BoundedLogFile? applicationLog = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settingsStore);
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
        var windowTitleSource = new WindowTitleSource(
            new Win32WindowTitleCatalog(),
            settings.WindowTitle);
        var ingestKeyStore = new IngestKeyStore(paths.IngestKeyFilePath);
        var externalIngestHandler = new ExternalIngestHttpHandler(
            ingestKeyStore.LoadOrCreate(),
            externalLease);
        var activeSources = new ActiveSourceManager(
            [windowsMediaSource, spotifyApiSource, externalPushSource, windowTitleSource],
            settings.Source.ToDescriptor());

        var timeProvider = TimeProvider.System;
        var runtimeState = new HostRuntimeState(timeProvider);
        var store = new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), timeProvider.GetUtcNow()),
            CreateLogger<NowPlayingStore>(loggerProvider));
        var artworkCache = new ArtworkCache();
        var outputManager = new OutputManager(
            store,
            artworkCache,
            settings.Outputs,
            logger: CreateLogger<OutputManager>(loggerProvider));
        var coordinator = new NowPlayingCoordinator(
            activeSources,
            store,
            artworkCache,
            timeProvider: timeProvider,
            logger: CreateLogger<NowPlayingCoordinator>(loggerProvider));
        var healthService = new HostHealthService(
            runtimeState,
            store,
            coordinator,
            activeSources,
            timeProvider);
        var statusService = new HostStatusService(
            runtimeState,
            coordinator,
            activeSources,
            store);
        var appearanceState = new AppearanceState(settings.Appearance);
        var httpServer = new OverlayHttpServer(
            options,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
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
        var runtime = new OverlayRuntime(
            options,
            statusService,
            httpServer,
            outputManager,
            coordinator,
            runtimeState,
            CreateLogger<OverlayRuntime>(loggerProvider));

        var sources = new MediaSourceService(
            activeSources,
            windowsMediaSource,
            spotifyApiSource,
            externalPushSource,
            windowTitleSource);
        var spotify = new SpotifyConnectionWorkflow(
            spotifyAuthorizationService,
            settingsStore,
            sources,
            httpServer);
        var browserPlayer = new BrowserPlayerConnectionService(
            ingestKeyStore,
            externalIngestHandler,
            httpServer);
        var settingsWorkflow = new SettingsApplicationWorkflow(
            settingsStore,
            httpServer,
            spotifyAuthorizationService,
            sources,
            appearanceState,
            outputManager,
            CreateLogger<SettingsApplicationWorkflow>(loggerProvider));
        var trayController = new TrayMenuController(
            runtime,
            settingsWorkflow,
            sources,
            spotify,
            browserPlayer,
            outputManager,
            paths.LogDirectory);

        return new OverlayComposition(
            runtime,
            trayController,
            settingsWorkflow,
            sources,
            spotify,
            browserPlayer);
    }

    internal static OverlayRuntimeGraph BuildRuntime(
        HostOptions options,
        ISessionSource sessionSource,
        OverlayPageAsset pageAsset,
        SpotifyAuthorizationCallbackBroker? spotifyCallbackBroker = null,
        ExternalIngestHttpHandler? externalIngestHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessionSource);
        ArgumentNullException.ThrowIfNull(pageAsset);
        options.Validate();

        var callbackBroker = spotifyCallbackBroker ?? new SpotifyAuthorizationCallbackBroker();
        var timeProvider = TimeProvider.System;
        var runtimeState = new HostRuntimeState(timeProvider);
        var store = new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), timeProvider.GetUtcNow()));
        var artworkCache = new ArtworkCache();
        var outputs = new OutputManager(store, artworkCache, new OutputSettings());
        var coordinator = new NowPlayingCoordinator(
            sessionSource,
            store,
            artworkCache,
            timeProvider: timeProvider);
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
        var appearance = new AppearanceState(new AppearanceSettings());
        var httpServer = new OverlayHttpServer(
            options,
            pageAsset,
            store,
            artworkCache,
            healthService,
            appearance,
            new ConnectionLimiter(options.MaximumSseConnections),
            new ConnectionLimiter(options.MaximumConcurrentConnections),
            new ServerEndpointChangeBroadcaster(),
            callbackBroker,
            externalIngestHandler,
            NullLogger<OverlayHttpServer>.Instance);

        var runtime = new OverlayRuntime(
            options,
            statusService,
            httpServer,
            outputs,
            coordinator,
            runtimeState);
        return new OverlayRuntimeGraph(runtime, httpServer, appearance);
    }

    private static ILogger<T> CreateLogger<T>(BoundedFileLoggerProvider? provider)
    {
        return provider?.CreateLogger<T>() ?? NullLogger<T>.Instance;
    }
}
