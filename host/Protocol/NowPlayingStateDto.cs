using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Protocol;

internal sealed record NowPlayingStateDto
{
    public const int CurrentProtocolVersion = 2;

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;

    [JsonPropertyName("serverInstanceId")]
    public required Guid ServerInstanceId { get; init; }

    [JsonPropertyName("snapshotRevision")]
    public required long SnapshotRevision { get; init; }

    [JsonPropertyName("source")]
    public SourceDto? Source { get; init; }

    [JsonPropertyName("playback")]
    public required ProtocolPlaybackState Playback { get; init; }

    [JsonPropertyName("track")]
    public TrackDto? Track { get; init; }

    [JsonPropertyName("artwork")]
    public ArtworkDto? Artwork { get; init; }

    [JsonPropertyName("observedAt")]
    public required DateTimeOffset ObservedAt { get; init; }
}
