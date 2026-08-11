using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.Windows;

internal sealed record WindowsMediaSessionSnapshot
{
    public required string SourceAppUserModelId { get; init; }

    public required MediaSessionPlaybackStatus PlaybackStatus { get; init; }

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? AlbumTitle { get; init; }

    public string? AlbumArtist { get; init; }

    public string? Subtitle { get; init; }

    public int TrackNumber { get; init; }

    public int AlbumTrackCount { get; init; }

    public MediaPlaybackKind? PlaybackType { get; init; }

    public IReadOnlyList<string?> Genres { get; init; } = Array.Empty<string?>();

    public IArtworkReader? ArtworkReader { get; init; }
}
