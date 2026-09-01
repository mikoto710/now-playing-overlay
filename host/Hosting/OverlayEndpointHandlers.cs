using System.Net;
using System.Text;
using System.Text.Json;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Protocol;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

/// <summary>
/// Implements the fixed loopback route table and endpoint response contracts. Routes are explicit
/// because their method, cache, and security behavior are part of the public overlay protocol.
/// </summary>
internal sealed class OverlayEndpointHandlers
{
    private const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    private const string ArtworkPrefix = "/api/v3/artwork/";

    private readonly OverlayPageAsset _pageAsset;
    private readonly BrowserProducerAsset _browserProducerAsset;
    private readonly NowPlayingStore _store;
    private readonly ArtworkCache _artworkCache;
    private readonly HostHealthService _healthService;
    private readonly AppearanceState _appearanceState;
    private readonly SseConnectionHandler _sse;
    private readonly SpotifyAuthorizationCallbackBroker _spotifyCallbackBroker;
    private readonly ExternalIngestHttpHandler? _externalIngestHandler;

    public OverlayEndpointHandlers(
        OverlayPageAsset pageAsset,
        BrowserProducerAsset browserProducerAsset,
        NowPlayingStore store,
        ArtworkCache artworkCache,
        HostHealthService healthService,
        AppearanceState appearanceState,
        SseConnectionHandler sse,
        SpotifyAuthorizationCallbackBroker spotifyCallbackBroker,
        ExternalIngestHttpHandler? externalIngestHandler)
    {
        _pageAsset = pageAsset ?? throw new ArgumentNullException(nameof(pageAsset));
        _browserProducerAsset = browserProducerAsset
            ?? throw new ArgumentNullException(nameof(browserProducerAsset));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _artworkCache = artworkCache ?? throw new ArgumentNullException(nameof(artworkCache));
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        _appearanceState = appearanceState
            ?? throw new ArgumentNullException(nameof(appearanceState));
        _sse = sse ?? throw new ArgumentNullException(nameof(sse));
        _spotifyCallbackBroker = spotifyCallbackBroker
            ?? throw new ArgumentNullException(nameof(spotifyCallbackBroker));
        _externalIngestHandler = externalIngestHandler;
    }

    public async Task DispatchAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var path = request.Url?.AbsolutePath ?? string.Empty;
        if (string.Equals(path, SpotifyAuthorizationRequest.RedirectPath, StringComparison.Ordinal))
        {
            await WriteSpotifyAuthorizationCallbackAsync(context, cancellationToken);
            return;
        }

        if (string.Equals(path, OverlayEndpoint.PagePath, StringComparison.Ordinal))
        {
            await LoopbackHttpResponseWriter.HandleGetAndHeadAsync(
                context,
                "text/html; charset=utf-8",
                "no-store",
                _pageAsset.Bytes,
                cancellationToken,
                ContentSecurityPolicy);
            return;
        }

        if (string.Equals(path, BrowserProducerAsset.Path, StringComparison.Ordinal))
        {
            await LoopbackHttpResponseWriter.HandleGetAndHeadAsync(
                context,
                "application/javascript; charset=utf-8",
                "no-store",
                _browserProducerAsset.Bytes,
                cancellationToken);
            return;
        }

        if (string.Equals(path, "/api/v3/state", StringComparison.Ordinal))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                NowPlayingStateMapper.Map(_store.Current),
                ProtocolJson.Options);
            await LoopbackHttpResponseWriter.HandleGetAndHeadAsync(
                context,
                "application/json; charset=utf-8",
                "no-store",
                bytes,
                cancellationToken);
            return;
        }

        if (string.Equals(path, "/api/v3/appearance", StringComparison.Ordinal))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                AppearanceDtoMapper.Map(_appearanceState.GetCurrent()),
                ProtocolJson.Options);
            await LoopbackHttpResponseWriter.HandleGetAndHeadAsync(
                context,
                "application/json; charset=utf-8",
                "no-store",
                bytes,
                cancellationToken);
            return;
        }

        if (path.StartsWith(ArtworkPrefix, StringComparison.Ordinal))
        {
            await WriteArtworkAsync(context, path[ArtworkPrefix.Length..], cancellationToken);
            return;
        }

        if (string.Equals(path, "/api/v3/events", StringComparison.Ordinal))
        {
            if (!LoopbackHttpResponseWriter.IsMethod(request, "GET"))
            {
                LoopbackHttpResponseWriter.WriteMethodNotAllowed(context.Response, "GET");
                return;
            }

            await _sse.HandleAsync(context, cancellationToken);
            return;
        }

        if (string.Equals(path, "/health", StringComparison.Ordinal))
        {
            var health = _healthService.GetHealth();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(health.Body, ProtocolJson.Options);
            await LoopbackHttpResponseWriter.HandleGetAndHeadAsync(
                context,
                "application/json; charset=utf-8",
                "no-store",
                bytes,
                cancellationToken,
                statusCode: health.StatusCode);
            return;
        }

        if (_externalIngestHandler is not null && TryGetExternalIngestKind(path, out var requestKind))
        {
            await _externalIngestHandler.HandleAsync(context, requestKind, cancellationToken);
            return;
        }

        context.Response.StatusCode = 404;
        context.Response.ContentLength64 = 0;
    }

    private async Task WriteSpotifyAuthorizationCallbackAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        var response = context.Response;
        if (!_spotifyCallbackBroker.HasPendingAuthorization)
        {
            response.StatusCode = 404;
            response.ContentLength64 = 0;
            return;
        }

        if (!LoopbackHttpResponseWriter.IsMethod(context.Request, "GET"))
        {
            LoopbackHttpResponseWriter.WriteMethodNotAllowed(response, "GET");
            return;
        }

        if (context.Request.Url is not Uri callbackUri
            || !_spotifyCallbackBroker.TryComplete(callbackUri, out var callbackResponse))
        {
            response.StatusCode = 404;
            response.ContentLength64 = 0;
            return;
        }

        var body = Encoding.UTF8.GetBytes(
            $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Now Playing Overlay</title></head><body><p>{WebUtility.HtmlEncode(callbackResponse.Message)}</p></body></html>");
        response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
        response.StatusCode = (int)callbackResponse.StatusCode;
        await LoopbackHttpResponseWriter.WriteBodyAsync(
            context,
            "text/html; charset=utf-8",
            "no-store",
            body,
            cancellationToken);
    }

    private async Task WriteArtworkAsync(
        HttpListenerContext context,
        string artworkId,
        CancellationToken cancellationToken)
    {
        if (!LoopbackHttpResponseWriter.IsGetOrHead(context.Request))
        {
            LoopbackHttpResponseWriter.WriteMethodNotAllowed(context.Response, "GET, HEAD");
            return;
        }

        if (!IsLowercaseSha256(artworkId) || !_artworkCache.TryGet(artworkId, out var entry))
        {
            context.Response.StatusCode = 404;
            context.Response.ContentLength64 = 0;
            return;
        }

        await LoopbackHttpResponseWriter.WriteBodyAsync(
            context,
            entry!.ContentType,
            "public, max-age=31536000, immutable",
            entry.Bytes,
            cancellationToken);
    }

    private static bool TryGetExternalIngestKind(string path, out ExternalIngestRequestKind requestKind)
    {
        if (string.Equals(path, ExternalIngestHttpHandler.StatePath, StringComparison.Ordinal))
        {
            requestKind = ExternalIngestRequestKind.State;
            return true;
        }

        if (string.Equals(path, ExternalIngestHttpHandler.HeartbeatPath, StringComparison.Ordinal))
        {
            requestKind = ExternalIngestRequestKind.Heartbeat;
            return true;
        }

        if (string.Equals(path, ExternalIngestHttpHandler.ArtworkPath, StringComparison.Ordinal))
        {
            requestKind = ExternalIngestRequestKind.Artwork;
            return true;
        }

        requestKind = default;
        return false;
    }

    private static bool IsLowercaseSha256(string value)
    {
        return value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
