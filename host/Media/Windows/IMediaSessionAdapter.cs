using NowPlayingOverlay.Host.Media.Sources;

namespace NowPlayingOverlay.Host.Media.Windows;

internal interface IMediaSessionAdapter : IDisposable
{
    event EventHandler? Changed;

    string SourceAppUserModelId { get; }

    MediaSessionPlaybackStatus GetPlaybackStatus();

    ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken);
}
