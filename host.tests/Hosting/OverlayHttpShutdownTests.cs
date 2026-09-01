using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.External;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.State;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed class OverlayHttpShutdownTests
{
    [Fact]
    public async Task HttpServerStopFailureStillDisposesIngestHandler()
    {
        var options = new HostOptions();
        var source = new FakeSessionSource();
        var store = new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), DateTimeOffset.UtcNow));
        await using var coordinator = new NowPlayingCoordinator(
            source,
            store,
            new ArtworkCache());
        var runtimeState = new HostRuntimeState(TimeProvider.System);
        var handler = new ExternalIngestHttpHandler(
            IngestKey.Generate(),
            new ExternalProducerLease(ExternalPushSource.DefaultLeaseDuration));
        var endpoint = new FaultingEndpoint(options.Port);
        await using var server = new FaultingOverlayHttpServer(
            endpoint,
            options,
            OverlayPageAsset.LoadEmbedded(typeof(OverlayPageAsset).Assembly),
            store,
            new ArtworkCache(),
            new HostHealthService(runtimeState, store, coordinator, source, TimeProvider.System),
            new AppearanceState(new AppearanceSettings()),
            new ConnectionLimiter(options.MaximumSseConnections),
            new ConnectionLimiter(options.MaximumConcurrentConnections),
            new ServerEndpointChangeBroadcaster(),
            new SpotifyAuthorizationCallbackBroker(),
            handler);
        await server.StartAsync();

        await Assert.ThrowsAsync<IOException>(() => server.DisposeAsync().AsTask());

        Assert.Equal(1, endpoint.StopCount);
        Assert.Throws<ObjectDisposedException>(() => handler.ExportKey());
        await server.DisposeAsync();
    }

    private sealed class FaultingOverlayHttpServer : OverlayHttpServer
    {
        private readonly ILoopbackListenerEndpoint _endpoint;

        public FaultingOverlayHttpServer(
            ILoopbackListenerEndpoint endpoint,
            HostOptions options,
            OverlayPageAsset pageAsset,
            NowPlayingStore store,
            ArtworkCache artworkCache,
            HostHealthService healthService,
            AppearanceState appearanceState,
            ConnectionLimiter sseLimiter,
            ConnectionLimiter requestLimiter,
            ServerEndpointChangeBroadcaster endpointChanges,
            SpotifyAuthorizationCallbackBroker spotifyCallbackBroker,
            ExternalIngestHttpHandler externalIngestHandler)
            : base(
                options,
                pageAsset,
                store,
                artworkCache,
                healthService,
                appearanceState,
                sseLimiter,
                requestLimiter,
                endpointChanges,
                spotifyCallbackBroker,
                externalIngestHandler,
                NullLogger<OverlayHttpServer>.Instance)
        {
            _endpoint = endpoint;
        }

        internal override ILoopbackListenerEndpoint CreateEndpoint(int port)
        {
            Assert.Equal(_endpoint.Port, port);
            return _endpoint;
        }
    }

    private sealed class FaultingEndpoint(int port) : ILoopbackListenerEndpoint
    {
        private bool _stopped;

        public int Port { get; } = port;

        public int StopCount { get; private set; }

        public void Start()
        {
        }

        public void CloseAfterFailedStart()
        {
            _stopped = true;
        }

        public Task StopAsync()
        {
            if (_stopped)
            {
                return Task.CompletedTask;
            }

            _stopped = true;
            StopCount++;
            return Task.FromException(new IOException("endpoint stop failed"));
        }
    }
}
