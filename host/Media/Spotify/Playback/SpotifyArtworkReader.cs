using NowPlayingOverlay.Host.Artwork;

namespace NowPlayingOverlay.Host.Media.Spotify.Playback;

internal sealed class SpotifyArtworkReader : IArtworkReader
{
    private readonly Uri _artworkUri;
    private readonly HttpClient _httpClient;

    public SpotifyArtworkReader(Uri artworkUri, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(artworkUri);
        if (!artworkUri.IsAbsoluteUri || artworkUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Spotify artwork must use an absolute HTTPS URL.", nameof(artworkUri));
        }

        _artworkUri = artworkUri;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async ValueTask<ArtworkPayload?> ReadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _artworkUri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode
            || response.Content.Headers.ContentLength is > ArtworkCacheOptions.DefaultMaximumItemBytes)
        {
            return null;
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > ArtworkCacheOptions.DefaultMaximumItemBytes)
            {
                return null;
            }

            destination.Write(buffer, 0, read);
        }

        return destination.Length == 0
            ? null
            : ArtworkPayload.Create(destination.GetBuffer().AsSpan(0, checked((int)destination.Length)));
    }
}
