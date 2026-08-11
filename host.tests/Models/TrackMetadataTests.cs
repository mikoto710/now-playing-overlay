using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Models;

public sealed class TrackMetadataTests
{
    [Fact]
    public void CreateNormalizesAllMediaFields()
    {
        var track = TrackMetadata.Create(
            " Cafe\u0301\r\n",
            " Artist\u2028Name ",
            " Album ",
            " Album artist ",
            " Subtitle ",
            trackNumber: 3,
            albumTrackCount: 12,
            playbackType: MediaPlaybackKind.Music,
            genres: [" Rock\r\n", "", "Rock", " Pop "]);

        Assert.Equal("Café", track.Title);
        Assert.Equal("Artist Name", track.Artist);
        Assert.Equal("Album", track.AlbumTitle);
        Assert.Equal("Album artist", track.AlbumArtist);
        Assert.Equal("Subtitle", track.Subtitle);
        Assert.Equal((uint)3, track.TrackNumber);
        Assert.Equal((uint)12, track.AlbumTrackCount);
        Assert.Equal(MediaPlaybackKind.Music, track.PlaybackType);
        Assert.Equal(["Rock", "Pop"], track.Genres);
    }

    [Fact]
    public void CreateAllowsMissingOptionalFieldsAndNormalizesZeroNumbers()
    {
        var track = TrackMetadata.Create(
            "Title",
            null,
            "  ",
            albumArtist: "\r\n",
            subtitle: null,
            trackNumber: 0,
            albumTrackCount: 0,
            genres: null);

        Assert.Equal(string.Empty, track.Artist);
        Assert.Null(track.AlbumTitle);
        Assert.Null(track.AlbumArtist);
        Assert.Null(track.Subtitle);
        Assert.Null(track.TrackNumber);
        Assert.Null(track.AlbumTrackCount);
        Assert.Null(track.PlaybackType);
        Assert.Empty(track.Genres);
    }

    [Fact]
    public void CreateRejectsEmptyNormalizedTitle()
    {
        Assert.Throws<ArgumentException>(() => TrackMetadata.Create("\r\n", "Artist", null));
    }

    [Fact]
    public void EqualityUsesNormalizedGenreContentInsteadOfCollectionIdentity()
    {
        var first = TrackMetadata.Create("Title", "Artist", null, genres: ["Rock", "Pop"]);
        var second = TrackMetadata.Create("Title", "Artist", null, genres: ["Rock", "Pop"]);
        var reordered = TrackMetadata.Create("Title", "Artist", null, genres: ["Pop", "Rock"]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, reordered);
    }

    [Fact]
    public void EqualityIncludesSupplementalMetadata()
    {
        var baseline = TrackMetadata.Create("Title", "Artist", "First album");
        var changed = TrackMetadata.Create("Title", "Artist", "Second album");

        Assert.NotEqual(baseline, changed);
    }

}
