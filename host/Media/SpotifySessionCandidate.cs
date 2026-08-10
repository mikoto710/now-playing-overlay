namespace NowPlayingOverlay.Host.Media;

internal sealed record SpotifySessionCandidate(
    IMediaSessionAdapter Session,
    string SourceAppUserModelId,
    MediaSessionPlaybackStatus? PlaybackStatus);
