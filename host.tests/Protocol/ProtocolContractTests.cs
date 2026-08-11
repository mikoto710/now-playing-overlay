using System.Text.Json;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Protocol;

namespace NowPlayingOverlay.Host.Tests.Protocol;

public sealed class ProtocolContractTests
{
    private static readonly Guid ServerInstanceId = Guid.Parse("f64b0c0f-73f3-4c0c-8b76-e84b89b77db2");

    [Fact]
    public void SerializePlayingSnapshotUsesFrozenVersionOneContract()
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
        var snapshot = NowPlayingSnapshot.Create(
            ServerInstanceId,
            42,
            "Spotify.exe",
            PlaybackState.Playing,
            track,
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
                "artwork",
                "observedAt",
            ],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(ServerInstanceId, root.GetProperty("serverInstanceId").GetGuid());
        Assert.Equal(42, root.GetProperty("snapshotRevision").GetInt64());
        Assert.Equal("spotify", root.GetProperty("source").GetString());
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

        var artworkJson = root.GetProperty("artwork");
        Assert.Equal(7, artworkJson.GetProperty("artworkRevision").GetInt64());
        Assert.Equal(
            "/api/v1/artwork/9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a",
            artworkJson.GetProperty("url").GetString());
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
        Assert.Equal(JsonValueKind.Null, root.GetProperty("artwork").ValueKind);
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
