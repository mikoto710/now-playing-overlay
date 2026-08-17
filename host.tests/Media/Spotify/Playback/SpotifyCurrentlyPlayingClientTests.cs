using System.Net;
using System.Text;
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
    public async Task RateLimitExposesTheServerRetryAfterDelay()
    {
        var response = new HttpResponseMessage((HttpStatusCode)429);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(17));
        using var httpClient = new HttpClient(new QueueHandler(new Queue<HttpResponseMessage>([response])));
        await using var client = CreateClient(httpClient);

        var error = await Assert.ThrowsAsync<SpotifyApiRequestException>(() =>
            client.GetCurrentlyPlayingAsync(
                new SpotifyClientId("client-id"),
                CancellationToken.None));

        Assert.Equal(SpotifyApiFailureKind.RateLimited, error.FailureKind);
        Assert.Equal(TimeSpan.FromSeconds(17), error.RetryAfter);
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

    private sealed class QueueHandler(
        Queue<HttpResponseMessage> responses,
        Action<HttpRequestMessage>? inspect = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            inspect?.Invoke(request);
            return Task.FromResult(responses.Dequeue());
        }
    }
}
