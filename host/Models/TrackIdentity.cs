using NowPlayingOverlay.Host.Media;

namespace NowPlayingOverlay.Host.Models;

internal sealed record TrackIdentity(
    SourceKey Source,
    string Title,
    string Artist,
    string? ProviderTrackId = null);
