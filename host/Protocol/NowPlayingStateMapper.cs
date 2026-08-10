using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Protocol;

internal static class NowPlayingStateMapper
{
    public static NowPlayingStateDto Map(NowPlayingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new NowPlayingStateDto
        {
            ServerInstanceId = snapshot.ServerInstanceId,
            SnapshotRevision = snapshot.SnapshotRevision,
            Source = snapshot.SourceAppUserModelId.Length == 0 ? null : "spotify",
            Playback = MapPlayback(snapshot.Playback),
            Track = snapshot.Track is null ? null : MapTrack(snapshot.Track),
            Artwork = snapshot.Artwork is null ? null : MapArtwork(snapshot.Artwork),
            ObservedAt = snapshot.ObservedAt,
        };
    }

    private static ProtocolPlaybackState MapPlayback(PlaybackState playback)
    {
        return playback switch
        {
            PlaybackState.Playing => ProtocolPlaybackState.Playing,
            PlaybackState.Paused => ProtocolPlaybackState.Paused,
            PlaybackState.Stopped => ProtocolPlaybackState.Stopped,
            PlaybackState.Idle => ProtocolPlaybackState.Idle,
            PlaybackState.Unavailable => ProtocolPlaybackState.Unavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(playback), playback, "Playback state is invalid."),
        };
    }

    private static TrackDto MapTrack(TrackMetadata track)
    {
        return new TrackDto
        {
            Title = track.Title,
            Artist = track.Artist,
            AlbumTitle = track.AlbumTitle,
            AlbumArtist = track.AlbumArtist,
            Subtitle = track.Subtitle,
            TrackNumber = track.TrackNumber,
            AlbumTrackCount = track.AlbumTrackCount,
            PlaybackType = track.PlaybackType is null ? null : MapPlaybackType(track.PlaybackType.Value),
            Genres = track.Genres.ToArray(),
        };
    }

    private static ProtocolMediaPlaybackKind MapPlaybackType(MediaPlaybackKind playbackType)
    {
        return playbackType switch
        {
            MediaPlaybackKind.Unknown => ProtocolMediaPlaybackKind.Unknown,
            MediaPlaybackKind.Music => ProtocolMediaPlaybackKind.Music,
            MediaPlaybackKind.Video => ProtocolMediaPlaybackKind.Video,
            MediaPlaybackKind.Image => ProtocolMediaPlaybackKind.Image,
            _ => throw new ArgumentOutOfRangeException(
                nameof(playbackType),
                playbackType,
                "Media playback type is invalid."),
        };
    }

    private static ArtworkDto MapArtwork(ArtworkDescriptor artwork)
    {
        return new ArtworkDto
        {
            ArtworkRevision = artwork.ArtworkRevision,
            ArtworkId = artwork.ArtworkId,
            Url = $"/api/v1/artwork/{artwork.ArtworkId}",
        };
    }
}
