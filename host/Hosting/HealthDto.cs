using System.Text.Json.Serialization;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed record HealthDto
{
    [JsonPropertyName("hostStatus")]
    public required string HostStatus { get; init; }

    [JsonPropertyName("activeSourceProvider")]
    public string? ActiveSourceProvider { get; init; }

    [JsonPropertyName("sourceStatus")]
    public required string SourceStatus { get; init; }

    [JsonPropertyName("serverInstanceId")]
    public required Guid ServerInstanceId { get; init; }

    [JsonPropertyName("snapshotRevision")]
    public required long SnapshotRevision { get; init; }

    [JsonPropertyName("uptimeSeconds")]
    public required long UptimeSeconds { get; init; }
}
