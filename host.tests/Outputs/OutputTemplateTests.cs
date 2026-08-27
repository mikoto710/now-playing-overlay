using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.Tests.Outputs;

public sealed class OutputTemplateTests
{
    [Fact]
    public void RendersUnifiedMetadataTokensAndEscapedBraces()
    {
        var observedAt = new DateTimeOffset(2026, 8, 27, 3, 4, 5, TimeSpan.Zero);
        var snapshot = CreateSnapshot(
            TrackMetadata.Create(
                "Track",
                "Artist",
                "Album",
                albumArtist: "Album Artist",
                subtitle: "Subtitle",
                trackNumber: 2,
                albumTrackCount: 10,
                genres: ["Rock", "Pop"]),
            observedAt,
            PlaybackTimeline.Create(65_000, 3_661_000, observedAt));
        var template = OutputTemplate.Parse(
            "{{{artist|upper}}} {title|lower} {albumTitle} {trackNumber}/{albumTrackCount} {genres} {playback} {source} {position}/{duration} {observedAt}",
            allowLineBreaks: true);

        var rendered = template.Render(snapshot);

        Assert.Equal(
            "{ARTIST} track Album 2/10 Rock, Pop playing windows-media 1:05/1:01:01 2026-08-27T03:04:05.0000000+00:00",
            rendered);
    }

    [Fact]
    public void NowPlayingDoesNotInventAnArtistSeparator()
    {
        var withArtist = OutputTemplate.Parse("{nowPlaying}", true).Render(
            CreateSnapshot(TrackMetadata.Create("Title", "Artist", null)));
        var titleOnly = OutputTemplate.Parse("{nowPlaying}", true).Render(
            CreateSnapshot(TrackMetadata.Create("Title", string.Empty, null)));

        Assert.Equal("Artist - Title", withArtist);
        Assert.Equal("Title", titleOnly);
    }

    [Fact]
    public void TruncateCountsUnicodeScalarsInsteadOfUtf16CodeUnits()
    {
        var rendered = OutputTemplate.Parse("{title|truncate:2}", true).Render(
            CreateSnapshot(TrackMetadata.Create("A😀B", string.Empty, null)));

        Assert.Equal("A😀", rendered);
    }

    [Theory]
    [InlineData("{unknown}")]
    [InlineData("{title")]
    [InlineData("title}")]
    [InlineData("{title|truncate:-1}")]
    [InlineData("{title|replace:x}")]
    public void RejectsUnknownOrMalformedSyntax(string value)
    {
        Assert.Throws<FormatException>(() => OutputTemplate.Parse(value, true));
    }

    [Theory]
    [InlineData("first\nsecond")]
    [InlineData("{newline}")]
    public void HistoryTemplatesRejectMultipleLines(string value)
    {
        Assert.Throws<FormatException>(() => OutputTemplate.Parse(value, false));
    }

    private static NowPlayingSnapshot CreateSnapshot(
        TrackMetadata track,
        DateTimeOffset? observedAt = null,
        PlaybackTimeline? timeline = null)
    {
        return NowPlayingSnapshot.Create(
            Guid.Parse("cc243c65-ab17-4c71-af59-2a3e18aa174a"),
            1,
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            track,
            timeline,
            artwork: null,
            observedAt ?? new DateTimeOffset(2026, 8, 27, 3, 4, 5, TimeSpan.Zero));
    }
}
