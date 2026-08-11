using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Media.Development;
using NowPlayingOverlay.Host.Media.Spotify;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Protocol;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

using OverlayHostOptions = Configuration.HostOptions;

internal static class OverlayApplication
{
    private static readonly string[] GetAndHead = [HttpMethods.Get, HttpMethods.Head];

    public static WebApplication Build(
        string[] args,
        int? persistedPort = null,
        BoundedLogFile? applicationLog = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (applicationLog is not null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new BoundedFileLoggerProvider(applicationLog));
        }

        var options = new OverlayHostOptions
        {
            Port = persistedPort ?? OverlayHostOptions.DefaultPort,
        };
        builder.Configuration.GetSection(OverlayHostOptions.SectionName).Bind(options);
        options.Validate();
        var pageAsset = OverlayPageAsset.Load(options, builder.Environment.ContentRootPath);

        builder.WebHost.ConfigureKestrel(server => ConfigureKestrel(server, options));
        builder.Services.AddHostFiltering(filter => filter.AllowedHosts = [OverlayHostOptions.AllowedHost]);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(pageAsset);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<HostRuntimeState>();
        builder.Services.AddSingleton(sp => new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(
                Guid.NewGuid(),
                sp.GetRequiredService<TimeProvider>().GetUtcNow()),
            sp.GetRequiredService<ILogger<NowPlayingStore>>()));
        builder.Services.AddSingleton<ArtworkCache>();
        RegisterSessionSource(builder.Services, options);
        builder.Services.AddSingleton<NowPlayingCoordinator>();
        builder.Services.AddSingleton<HostHealthService>();
        builder.Services.AddSingleton<HostStatusService>();
        builder.Services.AddSingleton(new SseConnectionLimiter(options.MaximumSseConnections));
        builder.Services.AddHostedService<OverlayRuntimeService>();

        var app = builder.Build();
        app.UseHostFiltering();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next(context);
        });
        MapEndpoints(app, options);
        return app;
    }

    private static void RegisterSessionSource(IServiceCollection services, OverlayHostOptions options)
    {
        if (options.SessionSource == Configuration.SessionSourceKind.Fake)
        {
            services.AddSingleton<FakeSessionSource>();
            services.AddSingleton<ISessionSource>(sp => sp.GetRequiredService<FakeSessionSource>());
            return;
        }

        services.AddSingleton<IMediaSessionManagerFactory, WindowsMediaSessionManagerFactory>();
        services.AddSingleton<SpotifySessionMatcher>();
        services.AddSingleton<SpotifySessionMonitor>();
        services.AddSingleton<ISessionSource>(sp => sp.GetRequiredService<SpotifySessionMonitor>());
    }

    private static void ConfigureKestrel(KestrelServerOptions server, OverlayHostOptions options)
    {
        server.Listen(IPAddress.Loopback, options.Port);
        server.Limits.MaxConcurrentConnections = options.MaximumConcurrentConnections;
        server.Limits.MaxConcurrentUpgradedConnections = options.MaximumSseConnections;
        server.Limits.MaxRequestHeaderCount = options.MaximumRequestHeaderCount;
        server.Limits.MaxRequestHeadersTotalSize = options.MaximumRequestHeadersTotalSize;
        server.Limits.RequestHeadersTimeout = options.RequestHeadersTimeout;
        server.Limits.KeepAliveTimeout = options.KeepAliveTimeout;
        server.AddServerHeader = false;
    }

    private static void MapEndpoints(WebApplication app, OverlayHostOptions options)
    {
        app.MapMethods("/NowPlaying.html", GetAndHead, WriteOverlayPageAsync);
        app.MapMethods("/api/v1/state", GetAndHead, WriteStateAsync);
        app.MapMethods("/api/v1/artwork/{artworkId}", GetAndHead, WriteArtworkAsync);
        app.MapGet("/api/v1/events", context => WriteEventsAsync(context, options));
        app.MapMethods("/health", GetAndHead, WriteHealthAsync);
    }

    private static async Task WriteOverlayPageAsync(HttpContext context, OverlayPageAsset pageAsset)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        await WriteBodyAsync(context, pageAsset.Bytes);
    }

    private static async Task WriteStateAsync(HttpContext context, NowPlayingStore store)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            NowPlayingStateMapper.Map(store.Current),
            ProtocolJson.Options);
        await WriteBodyAsync(context, bytes);
    }

    private static async Task WriteArtworkAsync(
        HttpContext context,
        string artworkId,
        ArtworkCache cache)
    {
        if (!IsLowercaseSha256(artworkId) || !cache.TryGet(artworkId, out var entry))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = entry!.ContentType;
        context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        await WriteBodyAsync(context, entry.Bytes);
    }

    private static async Task WriteHealthAsync(HttpContext context, HostHealthService healthService)
    {
        var health = healthService.GetHealth();
        context.Response.StatusCode = health.StatusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(health.Body, ProtocolJson.Options);
        await WriteBodyAsync(context, bytes);
    }

    private static async Task WriteEventsAsync(HttpContext context, OverlayHostOptions options)
    {
        var limiter = context.RequestServices.GetRequiredService<SseConnectionLimiter>();
        if (!limiter.TryAcquire(out var lease))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "1";
            return;
        }

        using (lease)
        // Reconnects always receive the current snapshot; Last-Event-ID never replays history.
        using (var subscription = context.RequestServices.GetRequiredService<NowPlayingStore>().Subscribe())
        using (var heartbeat = new PeriodicTimer(options.SseHeartbeatInterval))
        {
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Connection = "keep-alive";

            var cancellationToken = context.RequestAborted;
            var waitForSnapshot = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var waitForHeartbeat = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var completed = await Task.WhenAny(waitForSnapshot, waitForHeartbeat);
                    if (completed == waitForSnapshot)
                    {
                        if (!await waitForSnapshot)
                        {
                            break;
                        }

                        // The subscription is capacity one; draining publishes only the newest state.
                        while (subscription.Reader.TryRead(out var snapshot))
                        {
                            await WriteSseSnapshotAsync(context, snapshot, cancellationToken);
                        }

                        waitForSnapshot = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                    }

                    if (completed == waitForHeartbeat || waitForHeartbeat.IsCompletedSuccessfully)
                    {
                        if (!await waitForHeartbeat)
                        {
                            break;
                        }

                        await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken);
                        await context.Response.Body.FlushAsync(cancellationToken);
                        waitForHeartbeat = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task WriteSseSnapshotAsync(
        HttpContext context,
        NowPlayingSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var dto = NowPlayingStateMapper.Map(snapshot);
        var data = ProtocolJson.Serialize(dto);
        await context.Response.WriteAsync("event: state\n", cancellationToken);
        await context.Response.WriteAsync(
            $"id: {snapshot.ServerInstanceId:D}:{snapshot.SnapshotRevision}\n",
            cancellationToken);
        await context.Response.WriteAsync($"data: {data}\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteBodyAsync(HttpContext context, ReadOnlyMemory<byte> bytes)
    {
        context.Response.ContentLength = bytes.Length;
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        }
    }

    private static bool IsLowercaseSha256(string value)
    {
        return value.Length == 64
            && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
