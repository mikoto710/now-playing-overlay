using NowPlayingOverlay.Host.Media.Sources;

namespace NowPlayingOverlay.Host.Models;

internal sealed record TrackIdentity(
    SourceKey Source,
    string Title,
    string Artist,
    string? ProviderTrackId = null);
