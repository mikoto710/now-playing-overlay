namespace NowPlayingOverlay.Host.Media;

internal interface ISessionSource : IAsyncDisposable
{
    event EventHandler? Changed;

    ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken);
}
