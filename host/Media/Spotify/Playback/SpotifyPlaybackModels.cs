using System.Net;

namespace NowPlayingOverlay.Host.Media.Spotify.Playback;

internal enum SpotifyPlaybackResultKind
{
    Track,
    Idle,
    Unsupported,
}

internal sealed record SpotifyPlaybackTrack(
    string? TrackId,
    string Title,
    string Artist,
    string? AlbumTitle,
    Uri? ArtworkUri,
    bool IsPlaying);

internal sealed record SpotifyPlaybackResult(
    SpotifyPlaybackResultKind Kind,
    SpotifyPlaybackTrack? Track = null)
{
    public static SpotifyPlaybackResult Idle { get; } = new(SpotifyPlaybackResultKind.Idle);

    public static SpotifyPlaybackResult Unsupported { get; } = new(SpotifyPlaybackResultKind.Unsupported);

    public static SpotifyPlaybackResult FromTrack(SpotifyPlaybackTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new SpotifyPlaybackResult(SpotifyPlaybackResultKind.Track, track);
    }
}

internal enum SpotifyApiFailureKind
{
    Unauthorized,
    Forbidden,
    RateLimited,
    Transient,
    InvalidResponse,
}

internal sealed class SpotifyApiRequestException : Exception
{
    public SpotifyApiRequestException(
        SpotifyApiFailureKind failureKind,
        string message,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public SpotifyApiFailureKind FailureKind { get; }

    public HttpStatusCode? StatusCode { get; }

    public TimeSpan? RetryAfter { get; }
}
