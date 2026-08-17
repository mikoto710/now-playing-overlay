namespace NowPlayingOverlay.Host.Models;

internal sealed class TrackMetadata : IEquatable<TrackMetadata>
{
    private readonly IReadOnlyList<string> _genres;

    private TrackMetadata(
        string title,
        string artist,
        string? albumTitle,
        string? albumArtist,
        string? subtitle,
        uint? trackNumber,
        uint? albumTrackCount,
        MediaPlaybackKind? playbackType,
        IReadOnlyList<string> genres,
        string? providerTrackId)
    {
        Title = title;
        Artist = artist;
        AlbumTitle = albumTitle;
        AlbumArtist = albumArtist;
        Subtitle = subtitle;
        TrackNumber = trackNumber;
        AlbumTrackCount = albumTrackCount;
        PlaybackType = playbackType;
        _genres = genres;
        ProviderTrackId = providerTrackId;
    }

    public string Title { get; }

    public string Artist { get; }

    public string? AlbumTitle { get; }

    public string? AlbumArtist { get; }

    public string? Subtitle { get; }

    public uint? TrackNumber { get; }

    public uint? AlbumTrackCount { get; }

    public MediaPlaybackKind? PlaybackType { get; }

    public IReadOnlyList<string> Genres => _genres;

    public string? ProviderTrackId { get; }

    public static TrackMetadata Create(
        string? title,
        string? artist,
        string? albumTitle,
        string? albumArtist = null,
        string? subtitle = null,
        uint? trackNumber = null,
        uint? albumTrackCount = null,
        MediaPlaybackKind? playbackType = null,
        IEnumerable<string?>? genres = null,
        string? providerTrackId = null)
    {
        var normalizedTitle = MediaTextNormalizer.Normalize(title);
        if (normalizedTitle.Length == 0)
        {
            throw new ArgumentException("Track title must not be empty after normalization.", nameof(title));
        }

        var normalizedArtist = MediaTextNormalizer.Normalize(artist);
        var normalizedAlbumTitle = NormalizeOptional(albumTitle);
        var normalizedAlbumArtist = NormalizeOptional(albumArtist);
        var normalizedSubtitle = NormalizeOptional(subtitle);
        var normalizedGenres = NormalizeGenres(genres);
        var normalizedProviderTrackId = NormalizeOptional(providerTrackId);

        if (playbackType is not null && !Enum.IsDefined(playbackType.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(playbackType),
                playbackType,
                "Media playback type is invalid.");
        }

        return new TrackMetadata(
            normalizedTitle,
            normalizedArtist,
            normalizedAlbumTitle,
            normalizedAlbumArtist,
            normalizedSubtitle,
            NormalizePositiveNumber(trackNumber),
            NormalizePositiveNumber(albumTrackCount),
            playbackType,
            normalizedGenres,
            normalizedProviderTrackId);
    }

    public bool Equals(TrackMetadata? other)
    {
        // GSMTC may recreate Genres on each read, so equality compares values.
        return other is not null
            && string.Equals(Title, other.Title, StringComparison.Ordinal)
            && string.Equals(Artist, other.Artist, StringComparison.Ordinal)
            && string.Equals(AlbumTitle, other.AlbumTitle, StringComparison.Ordinal)
            && string.Equals(AlbumArtist, other.AlbumArtist, StringComparison.Ordinal)
            && string.Equals(Subtitle, other.Subtitle, StringComparison.Ordinal)
            && TrackNumber == other.TrackNumber
            && AlbumTrackCount == other.AlbumTrackCount
            && PlaybackType == other.PlaybackType
            && string.Equals(ProviderTrackId, other.ProviderTrackId, StringComparison.Ordinal)
            && Genres.SequenceEqual(other.Genres, StringComparer.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is TrackMetadata other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Title, StringComparer.Ordinal);
        hash.Add(Artist, StringComparer.Ordinal);
        hash.Add(AlbumTitle, StringComparer.Ordinal);
        hash.Add(AlbumArtist, StringComparer.Ordinal);
        hash.Add(Subtitle, StringComparer.Ordinal);
        hash.Add(TrackNumber);
        hash.Add(AlbumTrackCount);
        hash.Add(PlaybackType);
        hash.Add(ProviderTrackId, StringComparer.Ordinal);
        foreach (var genre in Genres)
        {
            hash.Add(genre, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = MediaTextNormalizer.Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static uint? NormalizePositiveNumber(uint? value)
    {
        return value is null or 0 ? null : value;
    }

    private static IReadOnlyList<string> NormalizeGenres(IEnumerable<string?>? genres)
    {
        if (genres is null)
        {
            return Array.Empty<string>();
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var genre in genres)
        {
            var value = MediaTextNormalizer.Normalize(genre);
            if (value.Length > 0 && seen.Add(value))
            {
                normalized.Add(value);
            }
        }

        return normalized.AsReadOnly();
    }
}
