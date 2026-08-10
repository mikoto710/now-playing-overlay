using System.Threading.Channels;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.State;

internal sealed class NowPlayingSubscription : IDisposable
{
    private Action? _dispose;

    internal NowPlayingSubscription(
        ChannelReader<NowPlayingSnapshot> reader,
        Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<NowPlayingSnapshot> Reader { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
