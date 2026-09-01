using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.Sources;

/// <summary>
/// Immutable, validated boundary between provider objects and the state core.
/// </summary>
internal sealed class SessionObservation
{
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

        PlaybackStateInvariant.Validate(
            playback,
            source,
            track,
            timeline,
            artworkReader is not null);
    }
}
