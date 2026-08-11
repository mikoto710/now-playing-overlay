using System.Text;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media;

internal sealed class SessionObservation
{
    // This value-only boundary keeps platform objects out of the state core.
    private SessionObservation(
        string sourceAppUserModelId,
        PlaybackState playback,
        TrackMetadata? track,
        IArtworkReader? artworkReader)
    {
        SourceAppUserModelId = sourceAppUserModelId;
        Playback = playback;
        Track = track;
        ArtworkReader = artworkReader;
    }

    public string SourceAppUserModelId { get; }

    public PlaybackState Playback { get; }

    public TrackMetadata? Track { get; }

    public IArtworkReader? ArtworkReader { get; }

    public TrackIdentity? Identity =>
        Track is null ? null : TrackIdentity.Create(SourceAppUserModelId, Track);

    public static SessionObservation Create(
        string? sourceAppUserModelId,
        PlaybackState playback,
        TrackMetadata? track = null,
        IArtworkReader? artworkReader = null)
    {
        var source = NormalizeSource(sourceAppUserModelId);
        Validate(playback, source, track, artworkReader);
        return new SessionObservation(source, playback, track, artworkReader);
    }

    private static string NormalizeSource(string? sourceAppUserModelId)
    {
        return string.IsNullOrWhiteSpace(sourceAppUserModelId)
            ? string.Empty
            : sourceAppUserModelId.Trim().Normalize(NormalizationForm.FormC);
    }

    private static void Validate(
        PlaybackState playback,
        string source,
        TrackMetadata? track,
        IArtworkReader? artworkReader)
    {
        if (artworkReader is not null && track is null)
        {
            throw new ArgumentException("Artwork reader requires track metadata.", nameof(artworkReader));
        }

        switch (playback)
        {
            case PlaybackState.Playing when source.Length == 0 || track is null:
                throw new ArgumentException("Playing requires a source and track metadata.");
            case PlaybackState.Paused or PlaybackState.Stopped when source.Length == 0:
                throw new ArgumentException($"{playback} requires a source.");
            case PlaybackState.Idle when source.Length == 0 || track is not null || artworkReader is not null:
                throw new ArgumentException("Idle requires a source without track metadata or artwork.");
            case PlaybackState.Unavailable when source.Length != 0 || track is not null || artworkReader is not null:
                throw new ArgumentException("Unavailable must not contain a source, track, or artwork.");
            case < PlaybackState.Playing or > PlaybackState.Unavailable:
                throw new ArgumentOutOfRangeException(nameof(playback), playback, "Playback state is invalid.");
        }
    }
}
