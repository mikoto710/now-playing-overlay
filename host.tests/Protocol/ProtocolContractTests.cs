using System.Text.Json;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Protocol;

namespace NowPlayingOverlay.Host.Tests.Protocol;

public sealed class ProtocolContractTests
{
    private static readonly Guid ServerInstanceId = Guid.Parse("f64b0c0f-73f3-4c0c-8b76-e84b89b77db2");

    [Fact]
    public void SerializePlayingSnapshotUsesFrozenVersionThreeContractWithoutInternalIdentity()
    {
        var track = TrackMetadata.Create(
            "Track title",
            "Artist name",
            "Album name",
            "Album artist",
            subtitle: null,
            trackNumber: 3,
            albumTrackCount: 12,
            playbackType: MediaPlaybackKind.Music,
            genres: ["Rock", "Pop"]);
        var artwork = new ArtworkDescriptor(
            7,
            "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a",
            "image/png",
            1024);
        var sampledAt = new DateTimeOffset(2026, 8, 9, 11, 59, 59, TimeSpan.Zero);
        var timeline = PlaybackTimeline.Create(100_000, 240_000, sampledAt);
        var snapshot = NowPlayingSnapshot.Create(
            ServerInstanceId,
            42,
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            track,
            timeline,
            artwork,
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));

        using var document = JsonDocument.Parse(ProtocolJson.Serialize(NowPlayingStateMapper.Map(snapshot)));
        var root = document.RootElement;

        Assert.Equal(
            [
                "protocolVersion",
                "serverInstanceId",
                "snapshotRevision",
                "source",
                "playback",
                "track",
                "timeline",
                "artwork",
                "observedAt",
            ],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(3, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(ServerInstanceId, root.GetProperty("serverInstanceId").GetGuid());
        Assert.Equal(42, root.GetProperty("snapshotRevision").GetInt64());
        var sourceJson = root.GetProperty("source");
        Assert.Equal(["provider"], sourceJson.EnumerateObject().Select(property => property.Name));
        Assert.Equal("windows-media", sourceJson.GetProperty("provider").GetString());
        Assert.Equal("playing", root.GetProperty("playback").GetString());

        var trackJson = root.GetProperty("track");
        Assert.Equal(
            [
                "title",
                "artist",
                "albumTitle",
                "albumArtist",
                "subtitle",
                "trackNumber",
                "albumTrackCount",
                "playbackType",
                "genres",
            ],
            trackJson.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Album name", trackJson.GetProperty("albumTitle").GetString());
        Assert.Equal("Album artist", trackJson.GetProperty("albumArtist").GetString());
        Assert.Equal(JsonValueKind.Null, trackJson.GetProperty("subtitle").ValueKind);
        Assert.Equal(3u, trackJson.GetProperty("trackNumber").GetUInt32());
        Assert.Equal(12u, trackJson.GetProperty("albumTrackCount").GetUInt32());
        Assert.Equal("music", trackJson.GetProperty("playbackType").GetString());
        Assert.Equal(["Rock", "Pop"], trackJson.GetProperty("genres").EnumerateArray().Select(value => value.GetString()));

        var timelineJson = root.GetProperty("timeline");
        Assert.Equal(
            ["positionMs", "durationMs", "sampledAt"],
            timelineJson.EnumerateObject().Select(property => property.Name));
        Assert.Equal(100_000, timelineJson.GetProperty("positionMs").GetInt64());
        Assert.Equal(240_000, timelineJson.GetProperty("durationMs").GetInt64());
        Assert.Equal(sampledAt, timelineJson.GetProperty("sampledAt").GetDateTimeOffset());

        var artworkJson = root.GetProperty("artwork");
        Assert.Equal(7, artworkJson.GetProperty("artworkRevision").GetInt64());
        Assert.Equal(
            "/api/v3/artwork/9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a",
            artworkJson.GetProperty("url").GetString());
        Assert.DoesNotContain(
            "Player.App",
            root.GetRawText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeUnavailableSnapshotKeepsStableNullShape()
    {
        var snapshot = NowPlayingSnapshot.CreateInitial(
            ServerInstanceId,
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));

        using var document = JsonDocument.Parse(ProtocolJson.Serialize(NowPlayingStateMapper.Map(snapshot)));
        var root = document.RootElement;

        Assert.Equal("unavailable", root.GetProperty("playback").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("source").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("track").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("timeline").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("artwork").ValueKind);
    }

    [Fact]
    public void SerializeSelectedUnavailableSnapshotKeepsProviderDescriptor()
    {
        var snapshot = NowPlayingSnapshot.Create(
            ServerInstanceId,
            1,
            SourceDescriptor.WindowsMedia("Private.Player.Aumid"),
            PlaybackState.Unavailable,
            track: null,
            artwork: null,
            DateTimeOffset.UtcNow);

        using var document = JsonDocument.Parse(ProtocolJson.Serialize(NowPlayingStateMapper.Map(snapshot)));
        var root = document.RootElement;

        Assert.Equal(
            "windows-media",
            root.GetProperty("source").GetProperty("provider").GetString());
        Assert.DoesNotContain("Private.Player.Aumid", root.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("track").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("timeline").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("artwork").ValueKind);
    }

    [Theory]
    [InlineData((int)SourceProvider.WindowsMedia, "windows-media")]
    [InlineData((int)SourceProvider.SpotifyApi, "spotify-api")]
    public void EveryCurrentProviderUsesCanonicalProtocolToken(int value, string expected)
    {
        var token = ((SourceProvider)value).ToProtocolValue();

        Assert.Equal(expected, token);
        Assert.True(SourceProviderProtocolToken.IsCanonical(token));
    }

    [Theory]
    [InlineData("future-player", true)]
    [InlineData("", false)]
    [InlineData("Windows-media", false)]
    [InlineData("windows_media", false)]
    [InlineData("windows--media", false)]
    [InlineData("2player", false)]
    public void ProviderTokenValidationUsesFrozenCanonicalShape(string token, bool expected)
    {
        Assert.Equal(expected, SourceProviderProtocolToken.IsCanonical(token));
    }

    [Fact]
    public void ProviderTokenValidationUsesFrozenLengthBound()
    {
        Assert.Equal(64, SourceProviderProtocolToken.MaximumLength);
        Assert.True(SourceProviderProtocolToken.IsCanonical(new string('a', 64)));
        Assert.False(SourceProviderProtocolToken.IsCanonical(new string('a', 65)));
    }

    [Theory]
    [InlineData((int)ProtocolPlaybackState.Playing, "\"playing\"")]
    [InlineData((int)ProtocolPlaybackState.Paused, "\"paused\"")]
    [InlineData((int)ProtocolPlaybackState.Stopped, "\"stopped\"")]
    [InlineData((int)ProtocolPlaybackState.Idle, "\"idle\"")]
    [InlineData((int)ProtocolPlaybackState.Unavailable, "\"unavailable\"")]
    public void EveryPlaybackValueUsesLowercaseJson(
        int value,
        string expectedJson)
    {
        Assert.Equal(
            expectedJson,
            JsonSerializer.Serialize((ProtocolPlaybackState)value, ProtocolJson.Options));
    }

    [Theory]
    [InlineData((int)ProtocolMediaPlaybackKind.Unknown, "\"unknown\"")]
    [InlineData((int)ProtocolMediaPlaybackKind.Music, "\"music\"")]
    [InlineData((int)ProtocolMediaPlaybackKind.Video, "\"video\"")]
    [InlineData((int)ProtocolMediaPlaybackKind.Image, "\"image\"")]
    public void EveryMediaPlaybackValueUsesLowercaseJson(
        int value,
        string expectedJson)
    {
        Assert.Equal(
            expectedJson,
            JsonSerializer.Serialize((ProtocolMediaPlaybackKind)value, ProtocolJson.Options));
    }
}
