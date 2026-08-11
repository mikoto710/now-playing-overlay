using NowPlayingOverlay.Host.Media.Windows;

namespace NowPlayingOverlay.Host.Media.Spotify;

internal sealed record SpotifySessionCandidate(
    IMediaSessionAdapter Session,
    string SourceAppUserModelId,
    MediaSessionPlaybackStatus? PlaybackStatus);
