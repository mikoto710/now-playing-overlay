using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.External;

internal sealed record ExternalIngestState
{
    private ExternalIngestState(
        Guid producerId,
        long producerRevision,
        PlaybackState playback,
        TrackMetadata? track)
    {
        ProducerId = producerId;
        ProducerRevision = producerRevision;
        Playback = playback;
        Track = track;
    }

    public Guid ProducerId { get; }

    public long ProducerRevision { get; }

    public PlaybackState Playback { get; }

    public TrackMetadata? Track { get; }

    public static ExternalIngestState Create(
        Guid producerId,
        long producerRevision,
        PlaybackState playback,
        string? title = null,
        string? artist = null,
        string? albumTitle = null,
        string? trackId = null)
    {
        if (producerId == Guid.Empty)
        {
            throw new ArgumentException("Producer ID must not be empty.", nameof(producerId));
        }

        if (producerRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(producerRevision),
                producerRevision,
                "Producer revision must be positive.");
        }

        if (!Enum.IsDefined(playback))
        {
            throw new ArgumentOutOfRangeException(nameof(playback), playback, "Playback state is invalid.");
        }

        if (playback == PlaybackState.Unavailable)
        {
            throw new ArgumentException(
                "Unavailable is derived by the host and cannot be submitted by a Producer.",
                nameof(playback));
        }

        var hasTrackInput = title is not null
            || artist is not null
            || albumTitle is not null
            || trackId is not null;
        var track = hasTrackInput
            ? TrackMetadata.Create(title, artist, albumTitle, providerTrackId: trackId)
            : null;

        switch (playback)
        {
            case PlaybackState.Playing when track is null:
                throw new ArgumentException("Playing requires track metadata.", nameof(title));
            case PlaybackState.Idle when track is not null:
                throw new ArgumentException("Idle must not contain track metadata.", nameof(title));
        }

        return new ExternalIngestState(producerId, producerRevision, playback, track);
    }
}
