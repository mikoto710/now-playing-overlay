using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Media.Spotify.Playback;

internal sealed class SpotifyCurrentlyPlayingClient : IAsyncDisposable
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly Uri CurrentlyPlayingEndpoint =
        new("https://api.spotify.com/v1/me/player/currently-playing");

    private readonly Func<SpotifyClientId, bool, CancellationToken, Task<SpotifyAccessToken>> _getAccessToken;
    private readonly SpotifyAuthorizationService? _ownedAuthorizationService;
    private readonly HttpClient _httpClient;
    private readonly HttpClient? _ownedHttpClient;
    private int _disposeStarted;

    public SpotifyCurrentlyPlayingClient(
        SpotifyAuthorizationService authorizationService,
        HttpClient? httpClient = null)
    {
        _ownedAuthorizationService = authorizationService
            ?? throw new ArgumentNullException(nameof(authorizationService));
        _getAccessToken = authorizationService.GetAccessTokenAsync;
        _httpClient = httpClient ?? new HttpClient();
        _ownedHttpClient = httpClient is null ? _httpClient : null;
    }

    internal SpotifyCurrentlyPlayingClient(
        Func<SpotifyClientId, bool, CancellationToken, Task<SpotifyAccessToken>> getAccessToken,
        HttpClient httpClient)
    {
        _getAccessToken = getAccessToken ?? throw new ArgumentNullException(nameof(getAccessToken));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<SpotifyPlaybackResult> GetCurrentlyPlayingAsync(
        SpotifyClientId clientId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
        var token = await _getAccessToken(clientId, false, cancellationToken);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await SendAsync(token, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                token = await _getAccessToken(clientId, true, cancellationToken);
                continue;
            }

            return await ReadResponseAsync(response, cancellationToken);
        }

        throw new InvalidOperationException("Spotify authorization retry did not produce a response.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _ownedHttpClient?.Dispose();
        if (_ownedAuthorizationService is not null)
        {
            await _ownedAuthorizationService.DisposeAsync();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        SpotifyAccessToken token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CurrentlyPlayingEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static async Task<SpotifyPlaybackResult> ReadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return SpotifyPlaybackResult.Idle;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SpotifyApiRequestException(
                SpotifyApiFailureKind.Unauthorized,
                "Spotify rejected the refreshed access token.",
                response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new SpotifyApiRequestException(
                SpotifyApiFailureKind.Forbidden,
                "Spotify denied access to the currently-playing endpoint.",
                response.StatusCode);
        }

        if ((int)response.StatusCode == 429)
        {
            throw new SpotifyApiRequestException(
                SpotifyApiFailureKind.RateLimited,
                "Spotify rate-limited the currently-playing request.",
                response.StatusCode,
                GetRetryAfter(response));
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new SpotifyApiRequestException(
                SpotifyApiFailureKind.Transient,
                "Spotify is temporarily unavailable.",
                response.StatusCode);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new SpotifyApiRequestException(
                SpotifyApiFailureKind.InvalidResponse,
                $"Spotify returned unexpected HTTP status {(int)response.StatusCode}.",
                response.StatusCode);
        }

        try
        {
            var bytes = await ReadBoundedContentAsync(response.Content, cancellationToken);
            using var document = JsonDocument.Parse(bytes);
            return MapPayload(document.RootElement);
        }
        catch (SpotifyApiRequestException)
        {
            throw;
        }
        catch (Exception error) when (error is JsonException
            or InvalidDataException
            or InvalidOperationException
            or OverflowException)
        {
            throw new SpotifyApiRequestException(
                SpotifyApiFailureKind.InvalidResponse,
                "Spotify returned an invalid currently-playing response.",
                response.StatusCode,
                innerException: error);
        }
    }

    private static SpotifyPlaybackResult MapPayload(JsonElement root)
    {
        var playingType = GetOptionalString(root, "currently_playing_type");
        if (!string.Equals(playingType, "track", StringComparison.Ordinal))
        {
            return SpotifyPlaybackResult.Unsupported;
        }

        if (!root.TryGetProperty("item", out var item) || item.ValueKind == JsonValueKind.Null)
        {
            return SpotifyPlaybackResult.Idle;
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return SpotifyPlaybackResult.Unsupported;
        }

        var title = GetOptionalString(item, "name");
        if (string.IsNullOrWhiteSpace(title))
        {
            return SpotifyPlaybackResult.Unsupported;
        }

        var artists = ReadArtists(item);
        string? albumTitle = null;
        Uri? artworkUri = null;
        if (item.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object)
        {
            albumTitle = GetOptionalString(album, "name");
            artworkUri = ReadArtworkUri(album);
        }

        var isPlaying = root.TryGetProperty("is_playing", out var playing)
            && playing.ValueKind == JsonValueKind.True;
        return SpotifyPlaybackResult.FromTrack(new SpotifyPlaybackTrack(
            GetOptionalString(item, "id"),
            title,
            string.Join(", ", artists),
            albumTitle,
            artworkUri,
            isPlaying));
    }

    private static IReadOnlyList<string> ReadArtists(JsonElement item)
    {
        if (!item.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var artist in artists.EnumerateArray())
        {
            if (artist.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = GetOptionalString(artist, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static Uri? ReadArtworkUri(JsonElement album)
    {
        if (!album.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var image in images.EnumerateArray())
        {
            if (image.ValueKind == JsonValueKind.Object
                && Uri.TryCreate(GetOptionalString(image, "url"), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps)
            {
                return uri;
            }
        }

        return null;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException("Spotify response exceeded the allowed size.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException("Spotify response exceeded the allowed size.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }
}
