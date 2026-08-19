using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Protocol;

internal sealed record PlaybackTimelineDto
{
    [JsonPropertyName("positionMs")]
    public required long PositionMs { get; init; }

    [JsonPropertyName("durationMs")]
    public required long DurationMs { get; init; }

    [JsonPropertyName("sampledAt")]
    public required DateTimeOffset SampledAt { get; init; }
}
