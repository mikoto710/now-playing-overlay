using System.Net;
using System.Text;
using System.Threading.Channels;
using NowPlayingOverlay.Host.Media.Spotify;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.Spotify.Playback;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify.Playback;

public sealed class SpotifyCurrentlyPlayingClientTests
{
    [Fact]
    public async Task UnauthorizedResponseRefreshesOnceAndMapsTheTrackPayload()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.Unauthorized),
            JsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "currently_playing_type": "track",
                  "is_playing": true,
                  "item": {
                    "id": "spotify-track-id",
                    "name": "Track",
                    "artists": [{ "name": "First" }, { "name": "Second" }],
                    "album": {
                      "name": "Album",
                      "images": [{ "url": "https://i.scdn.co/image/cover" }]
                    },
                    "preview_url": "https://example.invalid/audio"
                  }
                }
                """),
        ]);
        var authorizations = new List<string?>();
        using var httpClient = new HttpClient(new QueueHandler(responses, request =>
            authorizations.Add(request.Headers.Authorization?.Parameter)));
        var refreshRequests = new List<bool>();
        await using var client = new SpotifyCurrentlyPlayingClient(
            (_, forceRefresh, _) =>
            {
                refreshRequests.Add(forceRefresh);
                return Task.FromResult(new SpotifyAccessToken(
                    forceRefresh ? "refreshed-token" : "cached-token",
                    DateTimeOffset.UtcNow.AddHours(1)));
            },
            httpClient);

        var result = await client.GetCurrentlyPlayingAsync(
            new SpotifyClientId("client-id"),
            CancellationToken.None);

        Assert.Equal([false, true], refreshRequests);
        Assert.Equal(["cached-token", "refreshed-token"], authorizations);
        Assert.Equal(SpotifyPlaybackResultKind.Track, result.Kind);
        Assert.Equal("spotify-track-id", result.Track!.TrackId);
        Assert.Equal("Track", result.Track.Title);
        Assert.Equal("First, Second", result.Track.Artist);
        Assert.Equal("Album", result.Track.AlbumTitle);
        Assert.Equal("https://i.scdn.co/image/cover", result.Track.ArtworkUri!.AbsoluteUri);
        Assert.True(result.Track.IsPlaying);
    }

    [Fact]
    public async Task ConcurrentPlayback429StopsTheForcedRefreshAfterA401()
    {
        var clientId = new SpotifyClientId("client-id");
        var firstReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bodyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishBody = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StreamContent(new ResponseStream(async (_, cancellationToken) =>
            {
                bodyStarted.SetResult();
                await finishBody.Task.WaitAsync(cancellationToken);
                return 0;
            })),
        };
        var sends = 0;
        var forcedRefreshes = 0;
        using var http = new HttpClient(new QueueHandler(_ => Interlocked.Increment(ref sends) == 1
            ? firstReply.Task : Task.FromResult(limited)));
        await using var client = new SpotifyCurrentlyPlayingClient((_, forceRefresh, _) =>
        {
            if (forceRefresh)
            {
                Interlocked.Increment(ref forcedRefreshes);
            }
            return Task.FromResult(new SpotifyAccessToken("cached", DateTimeOffset.UtcNow.AddHours(1)));
        }, http);

        var held = new QueuedContext();
        var previousContext = SynchronizationContext.Current;
        Task<SpotifyPlaybackResult> first;
        try
        {
            SynchronizationContext.SetSynchronizationContext(held);
            first = client.GetCurrentlyPlayingAsync(clientId, CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        var second = client.GetCurrentlyPlayingAsync(clientId, CancellationToken.None);
        firstReply.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await held.RunNextAsync(); // The second request can now receive its 429 headers.
        await bodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await held.RunNextAsync();
        held.RunQueued();
        finishBody.SetResult();
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
        while (!first.IsCompleted)
        {
            await held.RunNextAsync();
        }

        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => first);
        Assert.Equal(0, forcedRefreshes);
        Assert.Equal(2, sends);
    }

    [Fact]
    public async Task NoContentAndNonTrackPayloadsClearTheCurrentTrack()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            new(HttpStatusCode.NoContent),
            JsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "currently_playing_type": "episode",
                  "is_playing": true,
                  "item": { "name": "Episode" }
                }
                """),
        ]);
        using var httpClient = new HttpClient(new QueueHandler(responses));
        await using var client = CreateClient(httpClient);

        var idle = await client.GetCurrentlyPlayingAsync(
            new SpotifyClientId("client-id"),
            CancellationToken.None);
        var unsupported = await client.GetCurrentlyPlayingAsync(
            new SpotifyClientId("client-id"),
            CancellationToken.None);

        Assert.Equal(SpotifyPlaybackResultKind.Idle, idle.Kind);
        Assert.Equal(SpotifyPlaybackResultKind.Unsupported, unsupported.Kind);
    }

    [Fact]
    public async Task DeclaredOversizePlaybackBodyIsRejectedBeforeItIsRead()
    {
        var content = new StreamContent(new ResponseStream((_, _) => throw new InvalidOperationException("Unexpected body read.")));
        content.Headers.ContentLength = 1024 * 1024 + 1;
        var responses = new Queue<HttpResponseMessage>([new(HttpStatusCode.OK) { Content = content }]);
        using var http = new HttpClient(new QueueHandler(responses));
        await using var client = CreateClient(http);
        var error = await Assert.ThrowsAsync<SpotifyApiRequestException>(() =>
            client.GetCurrentlyPlayingAsync(new SpotifyClientId("client-id"), CancellationToken.None));
        Assert.Equal(SpotifyApiFailureKind.InvalidResponse, error.FailureKind);
    }

    [Fact]
    public async Task RateLimitUsesTheLocalMinimumWhenTheServerRequestsAShorterDelay()
    {
        var response = new HttpResponseMessage((HttpStatusCode)429);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(17));
        using var httpClient = new HttpClient(new QueueHandler(new Queue<HttpResponseMessage>([response])));
        await using var client = CreateClient(httpClient);

        var error = await Assert.ThrowsAsync<SpotifyRateLimitException>(() =>
            client.GetCurrentlyPlayingAsync(
                new SpotifyClientId("client-id"),
                CancellationToken.None));

        Assert.False(error.Cooldown.IsQuotaExceeded);
        Assert.InRange(error.RetryAfter.TotalSeconds, 30, 33);
    }

    [Fact]
    public async Task QuotaResponseIsClassifiedBeforeAnotherHttpRequestOrTokenRefresh()
    {
        var clock = new PolicyClock();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0);
        var requests = 0;
        var tokens = 0;
        using var httpClient = new HttpClient(new QueueHandler(new(
        [
            JsonResponse((HttpStatusCode)429, "{\"error\":{\"reason\":\"QUOTA_EXCEEDED\"}}"),
        ]), _ => requests++));
        await using var client = new SpotifyCurrentlyPlayingClient((_, _, _) =>
        {
            tokens++;
            return Task.FromResult(new SpotifyAccessToken("token", clock.GetUtcNow().AddHours(1)));
        }, httpClient, policy);
        var error = await Assert.ThrowsAsync<SpotifyRateLimitException>(() =>
            client.GetCurrentlyPlayingAsync(new SpotifyClientId("client-id"), CancellationToken.None));
        Assert.True(error.Cooldown.IsQuotaExceeded);
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() =>
            client.GetCurrentlyPlayingAsync(new SpotifyClientId("client-id"), CancellationToken.None));
        Assert.Equal(1, requests);
        Assert.Equal(1, tokens);
    }

    private static SpotifyCurrentlyPlayingClient CreateClient(HttpClient httpClient)
    {
        return new SpotifyCurrentlyPlayingClient(
            (_, _, _) => Task.FromResult(new SpotifyAccessToken(
                "access-token",
                DateTimeOffset.UtcNow.AddHours(1))),
            httpClient);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Channel<Action> _pending = Channel.CreateUnbounded<Action>();
        public override void Post(SendOrPostCallback callback, object? state) =>
            _pending.Writer.TryWrite(() => callback(state));

        public async Task RunNextAsync()
        {
            var callback = await _pending.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            callback();
        }

        public void RunQueued()
        {
            while (_pending.Reader.TryRead(out var callback))
            {
                callback();
            }
        }
    }

    private sealed class QueueHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public QueueHandler(Queue<HttpResponseMessage> responses, Action<HttpRequestMessage>? inspect = null)
            : this(request =>
            {
                inspect?.Invoke(request);
                return Task.FromResult(responses.Dequeue());
            }) { }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
