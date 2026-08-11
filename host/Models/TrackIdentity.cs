namespace NowPlayingOverlay.Host.Models;

internal sealed record TrackIdentity(
    string SourceAppUserModelId,
    string Title,
    string Artist);
