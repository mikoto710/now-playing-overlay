using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.State;

internal sealed class NowPlayingStore
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Channel<NowPlayingSnapshot>> _subscribers = [];
    private long _nextSubscriberId;
    private NowPlayingSnapshot _current;
    private readonly ILogger<NowPlayingStore> _logger;

    public NowPlayingStore(
        NowPlayingSnapshot initialSnapshot,
        ILogger<NowPlayingStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        if (initialSnapshot.SnapshotRevision != 0)
        {
            throw new ArgumentException("Initial snapshot revision must be zero.", nameof(initialSnapshot));
        }

        _current = initialSnapshot;
        _logger = logger ?? NullLogger<NowPlayingStore>.Instance;
    }

    public NowPlayingSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool TryCommit(
        SourceDescriptor? source,
        PlaybackState playback,
        TrackMetadata? track,
        ArtworkDescriptor? artwork,
        DateTimeOffset observedAt,
        out NowPlayingSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_current.SnapshotRevision == long.MaxValue)
            {
                throw new InvalidOperationException("Snapshot revision has reached its maximum value.");
            }

            var candidate = NowPlayingSnapshot.Create(
                _current.ServerInstanceId,
                _current.SnapshotRevision + 1,
                source,
                playback,
                track,
                artwork,
                observedAt);

            if (_current.HasSameVisibleStateAs(candidate))
            {
                snapshot = _current;
                return false;
            }

            _current = candidate;
            snapshot = candidate;
            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Writer.TryWrite(candidate);
            }
        }

        // Normal logs intentionally omit track text and artwork identifiers.
        _logger.LogInformation(
            "Committed snapshot revision {SnapshotRevision} with playback {Playback} and artwork presence {HasArtwork}.",
            snapshot.SnapshotRevision,
            snapshot.Playback,
            snapshot.Artwork is not null);
        return true;
    }

    public NowPlayingSubscription Subscribe()
    {
        // Slow consumers keep only the newest complete snapshot.
        var channel = Channel.CreateBounded<NowPlayingSnapshot>(
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
            channel.Writer.TryWrite(_current);
        }

        return new NowPlayingSubscription(
            channel.Reader,
            () => RemoveSubscriber(subscriberId, channel));
    }

    private void RemoveSubscriber(long subscriberId, Channel<NowPlayingSnapshot> channel)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriberId);
        }

        channel.Writer.TryComplete();
    }
}
