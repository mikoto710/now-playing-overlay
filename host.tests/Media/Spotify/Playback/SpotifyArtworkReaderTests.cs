using System.Net;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Media.Spotify.Playback;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify.Playback;

public sealed class SpotifyArtworkReaderTests
{
    [Fact]
    public async Task DownloadsOnlyBoundedArtworkWithoutAnApiAuthorizationHeader()
    {
        var authorizations = new List<string?>();
        var oversized = new ByteArrayContent([]);
        oversized.Headers.ContentLength = ArtworkCacheOptions.DefaultMaximumItemBytes + 1L;
        using var httpClient = new HttpClient(new QueueHandler(
            new Queue<HttpResponseMessage>(
            [
                new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) },
                new(HttpStatusCode.OK) { Content = oversized },
            ]),
            request => authorizations.Add(request.Headers.Authorization?.Parameter)));
        var reader = new SpotifyArtworkReader(new Uri("https://i.scdn.co/image/cover"), httpClient);

        var payload = await reader.ReadAsync(CancellationToken.None);
        var rejected = await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, payload!.Bytes.ToArray());
        Assert.Null(rejected);
        Assert.All(authorizations, Assert.Null);
    }

    private sealed class QueueHandler(
        Queue<HttpResponseMessage> responses,
        Action<HttpRequestMessage> inspect) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            inspect(request);
            return Task.FromResult(responses.Dequeue());
        }
    }
}
