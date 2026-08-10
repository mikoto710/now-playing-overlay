namespace NowPlayingOverlay.Host.Media;

internal interface IMediaSessionAdapter : IDisposable
{
    event EventHandler? Changed;

    string SourceAppUserModelId { get; }

    MediaSessionPlaybackStatus GetPlaybackStatus();

    ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken);
}
