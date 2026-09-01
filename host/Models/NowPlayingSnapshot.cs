using NowPlayingOverlay.Host.Media.Sources;

namespace NowPlayingOverlay.Host.Models;

/// <summary>
/// Immutable complete state published by the Store.
/// </summary>
internal sealed record NowPlayingSnapshot
{
    private const double PlayingTimelineToleranceMilliseconds = 500d;

    private NowPlayingSnapshot(
        Guid serverInstanceId,
        long snapshotRevision,
        SourceDescriptor? source,
        PlaybackState playback,
        TrackMetadata? track,
        PlaybackTimeline? timeline,
        ArtworkDescriptor? artwork,
        DateTimeOffset observedAt)
    {
        ServerInstanceId = serverInstanceId;
        SnapshotRevision = snapshotRevision;
        Source = source;
        Playback = playback;
        Track = track;
        Timeline = timeline;
        Artwork = artwork;
        ObservedAt = observedAt;
    }

    public Guid ServerInstanceId { get; }

    public long SnapshotRevision { get; }

    public SourceDescriptor? Source { get; }

    public PlaybackState Playback { get; }

    public TrackMetadata? Track { get; }

    public PlaybackTimeline? Timeline { get; }

    public ArtworkDescriptor? Artwork { get; }

    public DateTimeOffset ObservedAt { get; }

    public TrackIdentity? Identity =>
        Track is null
            ? null
            : new TrackIdentity(Source!.Key, Track.Title, Track.Artist, Track.ProviderTrackId);

    public static NowPlayingSnapshot CreateInitial(
        Guid serverInstanceId,
        DateTimeOffset observedAt)
    {
        return Create(
            serverInstanceId,
            snapshotRevision: 0,
            source: null,
            PlaybackState.Unavailable,
            track: null,
            timeline: null,
            artwork: null,
            observedAt);
    }

    public static NowPlayingSnapshot Create(
        Guid serverInstanceId,
        long snapshotRevision,
        SourceDescriptor? source,
        PlaybackState playback,
        TrackMetadata? track,
        ArtworkDescriptor? artwork,
        DateTimeOffset observedAt)
    {
        return Create(
            serverInstanceId,
            snapshotRevision,
            source,
            playback,
            track,
            timeline: null,
            artwork,
            observedAt);
    }

    public static NowPlayingSnapshot Create(
        Guid serverInstanceId,
        long snapshotRevision,
        SourceDescriptor? source,
        PlaybackState playback,
        TrackMetadata? track,
        PlaybackTimeline? timeline,
        ArtworkDescriptor? artwork,
        DateTimeOffset observedAt)
    {
        if (serverInstanceId == Guid.Empty)
        {
            throw new ArgumentException("Server instance ID must not be empty.", nameof(serverInstanceId));
        }

        if (snapshotRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshotRevision),
                snapshotRevision,
                "Snapshot revision must not be negative.");
        }

        ValidateState(playback, source, track, timeline, artwork);
        return new NowPlayingSnapshot(
            serverInstanceId,
            snapshotRevision,
            source,
            playback,
            track,
            timeline,
            artwork,
            observedAt.ToUniversalTime());
    }

    public bool HasSameVisibleStateAs(NowPlayingSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);

        // Revision and observation time describe a commit, not the user-visible state.
        return ServerInstanceId == other.ServerInstanceId
            && Equals(Source?.Key, other.Source?.Key)
            && Playback == other.Playback
            && Equals(Track, other.Track)
            && HasSameTimelineAs(other)
            && Artwork == other.Artwork;
    }

    private static void ValidateState(
        PlaybackState playback,
        SourceDescriptor? source,
        TrackMetadata? track,
        PlaybackTimeline? timeline,
        ArtworkDescriptor? artwork)
    {
        if (artwork is not null && track is null)
        {
            throw new ArgumentException("Artwork requires track metadata.", nameof(artwork));
        }

        PlaybackStateInvariant.Validate(
            playback,
            source,
            track,
            timeline,
            artwork is not null);
    }

    private bool HasSameTimelineAs(NowPlayingSnapshot other)
    {
        if (Timeline is null || other.Timeline is null)
        {
            return Timeline is null && other.Timeline is null;
        }

        if (Timeline.DurationMs != other.Timeline.DurationMs)
        {
            return false;
        }

        if (Playback == PlaybackState.Paused)
        {
            return Timeline.PositionMs == other.Timeline.PositionMs;
        }

        // A playing anchor is equivalent when it matches the projected old position.
        var elapsedMilliseconds = (other.Timeline.SampledAt - Timeline.SampledAt).TotalMilliseconds;
        var projectedPosition = Math.Clamp(
            Timeline.PositionMs + elapsedMilliseconds,
            0d,
            Timeline.DurationMs);
        return Math.Abs(projectedPosition - other.Timeline.PositionMs)
            <= PlayingTimelineToleranceMilliseconds;
    }
}
