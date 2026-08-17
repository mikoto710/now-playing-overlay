using System.Net;
using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.Spotify.Playback;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify.Playback;

public sealed class SpotifyApiSourceTests
{
    [Fact]
    public async Task ActiveSelectionPollsSingleFlightAfterCompletionAndDeactivationCancelsTheDelay()
    {
        var response = new TaskCompletionSource<SpotifyPlaybackResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var delay = new StepDelay();
        await using var source = CreateSource(
            (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return response.Task;
            },
            delay);

        source.SetSelection(SourceDescriptor.SpotifyApi());
        await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);
        Assert.Empty(delay.Requested);
        response.SetResult(SpotifyPlaybackResult.Idle);
        await WaitUntilAsync(() => delay.Requested.Count == 1);

        Assert.Equal(TimeSpan.FromSeconds(2.5), delay.Requested[0]);
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal(PlaybackState.Idle, (await source.ReadAsync(CancellationToken.None)).Playback);

        source.SetSelection(null);
        await delay.WaitForCancellationAsync(0);
        Assert.Equal(SourceStatus.Unconfigured, source.GetState().Status);
    }

    [Fact]
    public async Task NetworkFailuresPreserveARecentTrackThenClearItAfterTenSeconds()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero));
        var outcomes = new Queue<Func<SpotifyPlaybackResult>>(
        [
            () => SpotifyPlaybackResult.FromTrack(new SpotifyPlaybackTrack(
                "track-id",
                "Track",
                "Artist",
                "Album",
                null,
                true)),
            () => throw new HttpRequestException("offline"),
            () => throw new HttpRequestException("still offline"),
        ]);
        var delay = new StepDelay();
        await using var source = CreateSource(
            (_, _) => Task.FromResult(outcomes.Dequeue().Invoke()),
            delay,
            clock);
        source.SetSelection(SourceDescriptor.SpotifyApi());
        await WaitUntilAsync(() => delay.Requested.Count == 1);

        var playing = await source.ReadAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Playing, playing.Playback);
        Assert.Equal("track-id", playing.Identity!.ProviderTrackId);

        clock.Advance(TimeSpan.FromSeconds(5));
        delay.Release(0);
        await WaitUntilAsync(() => delay.Requested.Count == 2);
        Assert.Equal(SourceStatusReason.Stale, source.GetState().Reason);
        Assert.Equal(PlaybackState.Playing, (await source.ReadAsync(CancellationToken.None)).Playback);

        clock.Advance(TimeSpan.FromSeconds(6));
        delay.Release(1);
        await WaitUntilAsync(() => delay.Requested.Count == 3);
        Assert.Equal(SourceStatusReason.NetworkUnavailable, source.GetState().Reason);
        Assert.Equal(PlaybackState.Unavailable, (await source.ReadAsync(CancellationToken.None)).Playback);
        Assert.Equal(
            [TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)],
            delay.Requested);

        source.SetSelection(null);
        await delay.WaitForCancellationAsync(2);
    }

    [Fact]
    public async Task RateLimitClearsTheTrackAndUsesTheServerDelay()
    {
        var calls = 0;
        var delay = new StepDelay();
        await using var source = CreateSource(
            (_, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    return Task.FromResult(SpotifyPlaybackResult.FromTrack(new SpotifyPlaybackTrack(
                        "track-id",
                        "Track",
                        "Artist",
                        null,
                        null,
                        false)));
                }

                throw new SpotifyApiRequestException(
                    SpotifyApiFailureKind.RateLimited,
                    "limited",
                    (HttpStatusCode)429,
                    TimeSpan.FromSeconds(17));
            },
            delay);
        source.SetSelection(SourceDescriptor.SpotifyApi());
        await WaitUntilAsync(() => delay.Requested.Count == 1);
        delay.Release(0);
        await WaitUntilAsync(() => delay.Requested.Count == 2);

        Assert.Equal(SourceStatusReason.RateLimited, source.GetState().Reason);
        Assert.Equal(PlaybackState.Unavailable, (await source.ReadAsync(CancellationToken.None)).Playback);
        Assert.Equal(TimeSpan.FromSeconds(17), delay.Requested[1]);

        source.SetSelection(null);
        await delay.WaitForCancellationAsync(1);
    }

    private static SpotifyApiSource CreateSource(
        Func<SpotifyClientId, CancellationToken, Task<SpotifyPlaybackResult>> read,
        StepDelay delay,
        TimeProvider? timeProvider = null)
    {
        return new SpotifyApiSource(
            read,
            new SpotifyClientId("client-id"),
            new HttpClient(new RejectingHandler()),
            timeProvider ?? TimeProvider.System,
            delay.DelayAsync);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StepDelay
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource> _gates = [];
        private readonly List<TaskCompletionSource> _cancellations = [];
        private readonly List<TimeSpan> _requested = [];

        public IReadOnlyList<TimeSpan> Requested
        {
            get
            {
                lock (_gate)
                {
                    return _requested.ToArray();
                }
            }
        }

        public ValueTask DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            TaskCompletionSource gate;
            TaskCompletionSource cancellation;
            lock (_gate)
            {
                _requested.Add(duration);
                gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _gates.Add(gate);
                _cancellations.Add(cancellation);
            }

            return new ValueTask(WaitAsync(gate.Task, cancellation, cancellationToken));
        }

        public void Release(int index)
        {
            lock (_gate)
            {
                _gates[index].TrySetResult();
            }
        }

        public async Task WaitForCancellationAsync(int index)
        {
            Task task;
            lock (_gate)
            {
                task = _cancellations[index].Task;
            }

            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private static async Task WaitAsync(
            Task gate,
            TaskCompletionSource cancellation,
            CancellationToken cancellationToken)
        {
            try
            {
                await gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellation.TrySetResult();
                throw;
            }
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan duration)
        {
            UtcNow += duration;
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Artwork HTTP was not expected.");
        }
    }
}
