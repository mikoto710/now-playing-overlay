using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Protocol;

internal sealed record TrackDto
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("artist")]
    public required string Artist { get; init; }

    [JsonPropertyName("albumTitle")]
    public string? AlbumTitle { get; init; }

    [JsonPropertyName("albumArtist")]
    public string? AlbumArtist { get; init; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("trackNumber")]
    public uint? TrackNumber { get; init; }

    [JsonPropertyName("albumTrackCount")]
    public uint? AlbumTrackCount { get; init; }

    [JsonPropertyName("playbackType")]
    public ProtocolMediaPlaybackKind? PlaybackType { get; init; }

    [JsonPropertyName("genres")]
    public required IReadOnlyList<string> Genres { get; init; }
}
