using NowPlayingOverlay.Host.Media.Sources;

namespace NowPlayingOverlay.Host.Models;

/// <summary>
/// Shared legality checks for provider observations and committed snapshots.
/// </summary>
internal static class PlaybackStateInvariant
{
    public static void Validate(
        PlaybackState playback,
        SourceDescriptor? source,
        TrackMetadata? track,
        PlaybackTimeline? timeline,
        bool hasArtwork)
    {
        switch (playback)
        {
            case PlaybackState.Playing when source is null || track is null:
                throw new ArgumentException("Playing requires a source and track metadata.");
            case PlaybackState.Paused or PlaybackState.Stopped when source is null:
                throw new ArgumentException($"{playback} requires a source.");
            case PlaybackState.Idle when source is null || track is not null || hasArtwork:
                throw new ArgumentException("Idle requires a source without track metadata or artwork.");
            case PlaybackState.Unavailable when track is not null || hasArtwork:
                throw new ArgumentException("Unavailable must not contain track metadata or artwork.");
            case < PlaybackState.Playing or > PlaybackState.Unavailable:
                throw new ArgumentOutOfRangeException(
                    nameof(playback),
                    playback,
                    "Playback state is invalid.");
        }

        if (timeline is not null
            && playback is not (PlaybackState.Playing or PlaybackState.Paused))
        {
            throw new ArgumentException(
                $"{playback} must not contain a playback timeline.",
                nameof(timeline));
        }
    }
}
