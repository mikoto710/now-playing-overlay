namespace NowPlayingOverlay.Host.Hosting;

internal sealed class SseConnectionLimiter(int maximumConnections)
{
    private int _activeConnections;

    public bool TryAcquire(out IDisposable? lease)
    {
        while (true)
        {
            var current = Volatile.Read(ref _activeConnections);
            if (current >= maximumConnections)
            {
                lease = null;
                return false;
            }

            if (Interlocked.CompareExchange(ref _activeConnections, current + 1, current) == current)
            {
                lease = new Lease(this);
                return true;
            }
        }
    }

    private sealed class Lease(SseConnectionLimiter owner) : IDisposable
    {
        private SseConnectionLimiter? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is not null)
            {
                Interlocked.Decrement(ref current._activeConnections);
            }
        }
    }
}
