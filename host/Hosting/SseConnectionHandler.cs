using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Protocol;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Hosting;

/// <summary>
/// Owns one full-snapshot SSE connection. State delivery is latest-wins through a capacity-one
/// store subscription; endpoint changes are also coalesced and terminate the old connection after
/// directing the client to reconnect. No event history is replayed from Last-Event-ID.
/// </summary>
internal sealed class SseConnectionHandler
{
    private readonly TimeSpan _heartbeatInterval;
    private readonly NowPlayingStore _store;
    private readonly ConnectionLimiter _connectionLimiter;
    private readonly ServerEndpointChangeBroadcaster _endpointChanges;

    public SseConnectionHandler(
        TimeSpan heartbeatInterval,
        NowPlayingStore store,
        ConnectionLimiter connectionLimiter,
        ServerEndpointChangeBroadcaster endpointChanges)
    {
        _heartbeatInterval = heartbeatInterval;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _connectionLimiter = connectionLimiter
            ?? throw new ArgumentNullException(nameof(connectionLimiter));
        _endpointChanges = endpointChanges
            ?? throw new ArgumentNullException(nameof(endpointChanges));
    }

    public async Task HandleAsync(
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        if (!_connectionLimiter.TryAcquire(out var lease))
        {
            context.Response.StatusCode = 503;
            context.Response.Headers[HttpResponseHeader.RetryAfter] = "1";
            context.Response.ContentLength64 = 0;
            return;
        }

        using (lease)
        using (var subscription = _store.Subscribe())
        using (var endpointSubscription = _endpointChanges.Subscribe())
        using (var heartbeat = new PeriodicTimer(_heartbeatInterval))
        {
            var response = context.Response;
            response.StatusCode = 200;
            response.ContentType = "text/event-stream";
            response.Headers[HttpResponseHeader.CacheControl] = "no-store";
            response.SendChunked = true;
            response.KeepAlive = true;

            var waitForSnapshot = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var waitForHeartbeat = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
            var waitForEndpoint = endpointSubscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            while (!cancellationToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(waitForSnapshot, waitForHeartbeat, waitForEndpoint);
                if (completed == waitForSnapshot)
                {
                    if (!await waitForSnapshot)
                    {
                        break;
                    }

                    // Capacity one deliberately drops superseded states before serialization.
                    while (subscription.Reader.TryRead(out var snapshot))
                    {
                        await WriteSnapshotAsync(response, snapshot, cancellationToken);
                    }

                    waitForSnapshot = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                }

                if (completed == waitForEndpoint)
                {
                    if (!await waitForEndpoint)
                    {
                        break;
                    }

                    string? overlayUrl = null;
                    while (endpointSubscription.Reader.TryRead(out var changedUrl))
                    {
                        overlayUrl = changedUrl;
                    }

                    if (overlayUrl is not null)
                    {
                        await WriteServerEndpointAsync(response, overlayUrl, cancellationToken);
                        break;
                    }

                    waitForEndpoint = endpointSubscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                }

                if (completed == waitForHeartbeat || waitForHeartbeat.IsCompletedSuccessfully)
                {
                    if (!await waitForHeartbeat)
                    {
                        break;
                    }

                    await LoopbackHttpResponseWriter.WriteUtf8Async(
                        response,
                        ": heartbeat\n\n",
                        cancellationToken);
                    waitForHeartbeat = heartbeat.WaitForNextTickAsync(cancellationToken).AsTask();
                }
            }
        }
    }

    private static Task WriteSnapshotAsync(
        HttpListenerResponse response,
        NowPlayingSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var data = ProtocolJson.Serialize(NowPlayingStateMapper.Map(snapshot));
        return LoopbackHttpResponseWriter.WriteUtf8Async(
            response,
            $"event: state\nid: {snapshot.ServerInstanceId:D}:{snapshot.SnapshotRevision}\ndata: {data}\n\n",
            cancellationToken);
    }

    private static Task WriteServerEndpointAsync(
        HttpListenerResponse response,
        string overlayUrl,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(new ServerEndpointDto(overlayUrl), ProtocolJson.Options);
        return LoopbackHttpResponseWriter.WriteUtf8Async(
            response,
            $"event: server\ndata: {data}\n\n",
            cancellationToken);
    }

    private sealed record ServerEndpointDto(
        [property: JsonPropertyName("overlayUrl")] string OverlayUrl);
}
