using System.Text;

namespace NowPlayingOverlay.Host.Models;

internal sealed record TrackIdentity
{
    private TrackIdentity(string sourceAppUserModelId, string title, string artist)
    {
        SourceAppUserModelId = sourceAppUserModelId;
        Title = title;
        Artist = artist;
    }

    public string SourceAppUserModelId { get; }

    public string Title { get; }

    public string Artist { get; }

    public static TrackIdentity Create(string sourceAppUserModelId, TrackMetadata track)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAppUserModelId);

        var normalizedSource = sourceAppUserModelId.Trim().Normalize(NormalizationForm.FormC);
        return new TrackIdentity(normalizedSource, track.Title, track.Artist);
    }
}
