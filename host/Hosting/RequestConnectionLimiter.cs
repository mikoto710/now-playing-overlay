namespace NowPlayingOverlay.Host.Hosting;

internal sealed class RequestConnectionLimiter(int maximumConnections)
{
    // HttpListener exposes accepted requests, not a configurable TCP socket cap. Holding this
    // lease through the whole response keeps normal requests and long-lived SSE streams bounded,
    // and one shared instance preserves the limit while old and new ports overlap during rebinding.
    private int _activeRequests;

    public bool TryAcquire(out IDisposable? lease)
    {
        while (true)
        {
            var current = Volatile.Read(ref _activeRequests);
            if (current >= maximumConnections)
            {
                lease = null;
                return false;
            }

            if (Interlocked.CompareExchange(ref _activeRequests, current + 1, current) == current)
            {
                lease = new Lease(this);
                return true;
            }
        }
    }

    private sealed class Lease(RequestConnectionLimiter owner) : IDisposable
    {
        private RequestConnectionLimiter? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is not null)
            {
                Interlocked.Decrement(ref current._activeRequests);
            }
        }
    }
}
