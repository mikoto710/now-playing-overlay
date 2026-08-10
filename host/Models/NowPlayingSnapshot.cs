using System.Text;

namespace NowPlayingOverlay.Host.Models;

internal sealed record NowPlayingSnapshot
{
    private NowPlayingSnapshot(
        Guid serverInstanceId,
        long snapshotRevision,
        string sourceAppUserModelId,
        PlaybackState playback,
        TrackMetadata? track,
        ArtworkDescriptor? artwork,
        DateTimeOffset observedAt)
    {
        ServerInstanceId = serverInstanceId;
        SnapshotRevision = snapshotRevision;
        SourceAppUserModelId = sourceAppUserModelId;
        Playback = playback;
        Track = track;
        Artwork = artwork;
        ObservedAt = observedAt;
    }

    public Guid ServerInstanceId { get; }

    public long SnapshotRevision { get; }

    public string SourceAppUserModelId { get; }

    public PlaybackState Playback { get; }

    public TrackMetadata? Track { get; }

    public ArtworkDescriptor? Artwork { get; }

    public DateTimeOffset ObservedAt { get; }

    public TrackIdentity? Identity =>
        Track is null ? null : TrackIdentity.Create(SourceAppUserModelId, Track);

    public static NowPlayingSnapshot CreateInitial(
        Guid serverInstanceId,
        DateTimeOffset observedAt)
    {
        return Create(
            serverInstanceId,
            snapshotRevision: 0,
            sourceAppUserModelId: string.Empty,
            PlaybackState.Unavailable,
            track: null,
            artwork: null,
            observedAt);
    }

    public static NowPlayingSnapshot Create(
        Guid serverInstanceId,
        long snapshotRevision,
        string? sourceAppUserModelId,
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

        var source = NormalizeSource(sourceAppUserModelId);
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
            && string.Equals(SourceAppUserModelId, other.SourceAppUserModelId, StringComparison.Ordinal)
            && Playback == other.Playback
            && Equals(Track, other.Track)
            && Artwork == other.Artwork;
    }

    private static string NormalizeSource(string? sourceAppUserModelId)
    {
        return string.IsNullOrWhiteSpace(sourceAppUserModelId)
            ? string.Empty
            : sourceAppUserModelId.Trim().Normalize(NormalizationForm.FormC);
    }

    private static void ValidateState(
        PlaybackState playback,
        string source,
        TrackMetadata? track,
        ArtworkDescriptor? artwork)
    {
        if (artwork is not null && track is null)
        {
            throw new ArgumentException("Artwork requires track metadata.", nameof(artwork));
        }

        switch (playback)
        {
            case PlaybackState.Playing when source.Length == 0 || track is null:
                throw new ArgumentException("Playing requires a source and track metadata.");
            case PlaybackState.Paused or PlaybackState.Stopped when source.Length == 0:
                throw new ArgumentException($"{playback} requires a source.");
            case PlaybackState.Idle when source.Length == 0 || track is not null || artwork is not null:
                throw new ArgumentException("Idle requires a source without track metadata or artwork.");
            case PlaybackState.Unavailable when source.Length != 0 || track is not null || artwork is not null:
                throw new ArgumentException("Unavailable must not contain a source, track, or artwork.");
            case < PlaybackState.Playing or > PlaybackState.Unavailable:
                throw new ArgumentOutOfRangeException(nameof(playback), playback, "Playback state is invalid.");
        }
    }
}
