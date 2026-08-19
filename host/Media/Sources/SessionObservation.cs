using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.Sources;

internal sealed class SessionObservation
{
    // This value-only boundary keeps platform objects out of the state core.
    private SessionObservation(
        SourceDescriptor? source,
        PlaybackState playback,
        TrackMetadata? track,
        PlaybackTimeline? timeline,
        IArtworkReader? artworkReader)
    {
        Source = source;
        Playback = playback;
        Track = track;
        Timeline = timeline;
        ArtworkReader = artworkReader;
    }

    public SourceDescriptor? Source { get; }

    public PlaybackState Playback { get; }

    public TrackMetadata? Track { get; }

    public PlaybackTimeline? Timeline { get; }

    public IArtworkReader? ArtworkReader { get; }

    public TrackIdentity? Identity =>
        Track is null
            ? null
            : new TrackIdentity(Source!.Key, Track.Title, Track.Artist, Track.ProviderTrackId);

    public static SessionObservation Create(
        SourceDescriptor? source,
        PlaybackState playback,
        TrackMetadata? track = null,
        IArtworkReader? artworkReader = null,
        PlaybackTimeline? timeline = null)
    {
        Validate(playback, source, track, timeline, artworkReader);
        return new SessionObservation(source, playback, track, timeline, artworkReader);
    }

    private static void Validate(
        PlaybackState playback,
        SourceDescriptor? source,
        TrackMetadata? track,
        PlaybackTimeline? timeline,
        IArtworkReader? artworkReader)
    {
        if (artworkReader is not null && track is null)
        {
            throw new ArgumentException("Artwork reader requires track metadata.", nameof(artworkReader));
        }

        switch (playback)
        {
            case PlaybackState.Playing when source is null || track is null:
                throw new ArgumentException("Playing requires a source and track metadata.");
            case PlaybackState.Paused or PlaybackState.Stopped when source is null:
                throw new ArgumentException($"{playback} requires a source.");
            case PlaybackState.Idle when source is null || track is not null || artworkReader is not null:
                throw new ArgumentException("Idle requires a source without track metadata or artwork.");
            case PlaybackState.Unavailable when track is not null || artworkReader is not null:
                throw new ArgumentException("Unavailable must not contain track metadata or artwork.");
            case < PlaybackState.Playing or > PlaybackState.Unavailable:
                throw new ArgumentOutOfRangeException(nameof(playback), playback, "Playback state is invalid.");
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
