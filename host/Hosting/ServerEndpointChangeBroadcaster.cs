using System.Threading.Channels;

namespace NowPlayingOverlay.Host.Hosting;

/// <summary>
/// Fans out the latest endpoint replacement to SSE subscribers.
/// </summary>
internal sealed class ServerEndpointChangeBroadcaster
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Channel<string>> _subscribers = [];
    private long _nextSubscriberId;

    public ServerEndpointChangeSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        long subscriberId;
        lock (_gate)
        {
            subscriberId = checked(++_nextSubscriberId);
            _subscribers.Add(subscriberId, channel);
        }

        return new ServerEndpointChangeSubscription(
            channel.Reader,
            () => Remove(subscriberId, channel));
    }

    public void Publish(string overlayUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayUrl);
        lock (_gate)
        {
            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Writer.TryWrite(overlayUrl);
            }
        }
    }

    private void Remove(long subscriberId, Channel<string> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriberId);
        }

        channel.Writer.TryComplete();
    }
}

/// <summary>Owns one endpoint-change channel registration.</summary>
internal sealed class ServerEndpointChangeSubscription(
    ChannelReader<string> reader,
    Action dispose) : IDisposable
{
    private Action? _dispose = dispose;

    public ChannelReader<string> Reader { get; } = reader;

    public void Dispose()
    {
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
