using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Protocol;

internal sealed record SourceDto
{
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }
}
