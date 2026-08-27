using System.Threading.Channels;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.State;

internal sealed class OrderedNowPlayingSubscription : IDisposable
{
    private readonly Action _dispose;
    private int _disposed;

    internal OrderedNowPlayingSubscription(
        ChannelReader<NowPlayingSnapshot> reader,
        Action dispose)
    {
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    public ChannelReader<NowPlayingSnapshot> Reader { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _dispose();
        }
    }
}
