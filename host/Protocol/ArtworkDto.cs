using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Protocol;

internal sealed record ArtworkDto
{
    [JsonPropertyName("artworkRevision")]
    public required long ArtworkRevision { get; init; }

    [JsonPropertyName("artworkId")]
    public required string ArtworkId { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}
