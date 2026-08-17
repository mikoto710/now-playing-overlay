namespace NowPlayingOverlay.Host.Media.Sources;

internal interface ISessionSource : IAsyncDisposable
{
    event EventHandler? Changed;

    ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken);
}
