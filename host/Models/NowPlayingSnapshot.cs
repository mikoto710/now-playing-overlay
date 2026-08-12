using NowPlayingOverlay.Host.Media;

namespace NowPlayingOverlay.Host.Models;

internal sealed record NowPlayingSnapshot
{
    private NowPlayingSnapshot(
        Guid serverInstanceId,
        long snapshotRevision,
        SourceDescriptor? source,
        PlaybackState playback,
        TrackMetadata? track,
        ArtworkDescriptor? artwork,
        DateTimeOffset observedAt)
    {
        ServerInstanceId = serverInstanceId;
        SnapshotRevision = snapshotRevision;
        Source = source;
        Playback = playback;
        Track = track;
        Artwork = artwork;
        ObservedAt = observedAt;
    }

    public Guid ServerInstanceId { get; }

    public long SnapshotRevision { get; }

    public SourceDescriptor? Source { get; }

    public PlaybackState Playback { get; }

    public TrackMetadata? Track { get; }

    public ArtworkDescriptor? Artwork { get; }

    public DateTimeOffset ObservedAt { get; }

    public TrackIdentity? Identity =>
        Track is null ? null : new TrackIdentity(Source!.Key, Track.Title, Track.Artist);

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

        ValidateState(playback, source, track, artwork);
        return new NowPlayingSnapshot(
            serverInstanceId,
            snapshotRevision,
            source,
            playback,
            track,
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
            && Artwork == other.Artwork;
    }

    private static void ValidateState(
        PlaybackState playback,
        SourceDescriptor? source,
        TrackMetadata? track,
        ArtworkDescriptor? artwork)
    {
        if (artwork is not null && track is null)
        {
            throw new ArgumentException("Artwork requires track metadata.", nameof(artwork));
        }

        switch (playback)
        {
            case PlaybackState.Playing when source is null || track is null:
                throw new ArgumentException("Playing requires a source and track metadata.");
            case PlaybackState.Paused or PlaybackState.Stopped when source is null:
                throw new ArgumentException($"{playback} requires a source.");
            case PlaybackState.Idle when source is null || track is not null || artwork is not null:
                throw new ArgumentException("Idle requires a source without track metadata or artwork.");
            case PlaybackState.Unavailable when track is not null || artwork is not null:
                throw new ArgumentException("Unavailable must not contain track metadata or artwork.");
            case < PlaybackState.Playing or > PlaybackState.Unavailable:
                throw new ArgumentOutOfRangeException(nameof(playback), playback, "Playback state is invalid.");
        }
    }
}
