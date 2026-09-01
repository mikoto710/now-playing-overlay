namespace NowPlayingOverlay.Host.Media.Sources;

/// <summary>
/// Supplies complete observations; <see cref="Changed"/> is a coalescible read-again signal.
/// </summary>
internal interface ISessionSource : IAsyncDisposable
{
    event EventHandler? Changed;

    ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken);
}
