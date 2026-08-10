namespace NowPlayingOverlay.Host.Hosting;

internal sealed class HostRuntimeState(TimeProvider timeProvider)
{
    private readonly DateTimeOffset _startedAt = timeProvider.GetUtcNow();
    private int _ready;

    public DateTimeOffset StartedAt => _startedAt;

    public bool IsReady => Volatile.Read(ref _ready) != 0;

    public void MarkReady()
    {
        Volatile.Write(ref _ready, 1);
    }
}
