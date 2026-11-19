using System.Net;
using NowPlayingOverlay.Host.Media.Spotify;
using NowPlayingOverlay.Host.Media.Sources;
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

        Assert.Equal(TimeSpan.FromSeconds(5), delay.Requested[0]);
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
            [TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)],
            delay.Requested);

        source.SetSelection(null);
        await delay.WaitForCancellationAsync(2);
    }

    [Fact]
    public async Task PlaybackTransitionsSelectTheNextIntervalWithoutOverlappingRequests()
    {
        const string playing = """{"currently_playing_type":"track","item":{"name":"Track"},"is_playing":true}""";
        const string paused = """{"currently_playing_type":"track","item":{"name":"Track"},"is_playing":false}""";
        var bodies = new Queue<string?>([playing, paused, null, """{"currently_playing_type":"episode"}""", playing]);
        using var http = new HttpClient(new PlaybackHandler(() => bodies.Dequeue() is { } json
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
            : new HttpResponseMessage(HttpStatusCode.NoContent)));
        var clock = new PolicyClock();
        var log = new PolicyLogger();
        using var policy = new SpotifyRequestPolicy(timeProvider: clock, logger: log);
        var clientId = new SpotifyClientId("client-id");
        await using var client = new SpotifyCurrentlyPlayingClient(
            (_, _, _) => Task.FromResult(new SpotifyAccessToken("cached", clock.GetUtcNow().AddHours(1))), http, policy);
        var delay = new StepDelay();
        await using var source = new SpotifyApiSource(client.GetCurrentlyPlayingAsync, clientId, http, clock,
            delay.DelayAsync, requestPolicy: policy);
        source.SetSelection(SourceDescriptor.SpotifyApi());
        var intervals = new[] { 3, 5, 5, 5, 3 };
        for (var index = 0; index < intervals.Length; index++)
        {
            await WaitUntilAsync(() => delay.Requested.Count > index);
            Assert.Equal(TimeSpan.FromSeconds(intervals[index]), delay.Requested[index]);
            clock.Advance(TimeSpan.FromMinutes(15));
            var expected = intervals[index] == 3 ? "playing:3s IntervalSeconds=3" : "inactive:5s IntervalSeconds=5";
            Assert.Contains("PollingState=" + expected, log.Messages.Last());
            if (index + 1 < intervals.Length)
            {
                delay.Release(index);
            }
        }

        source.SetSelection(null);
        await delay.WaitForCancellationAsync(4);
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

                throw new SpotifyRateLimitException(new SpotifyCooldown("connection", SpotifyEndpoint.CurrentlyPlaying,
                    false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), 1), TimeSpan.FromHours(1));
            },
            delay);
        source.SetSelection(SourceDescriptor.SpotifyApi());
        await WaitUntilAsync(() => delay.Requested.Count == 1);
        delay.Release(0);
        await WaitUntilAsync(() => delay.Requested.Count == 2);

        Assert.Equal(SourceStatusReason.RateLimited, source.GetState().Reason);
        Assert.Equal(PlaybackState.Unavailable, (await source.ReadAsync(CancellationToken.None)).Playback);
        Assert.Equal(TimeSpan.FromHours(1), delay.Requested[1]);

        source.SetSelection(null);
        await delay.WaitForCancellationAsync(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedOrStalledPlaybackBodyUsesNetworkBackoffAndRecovers(bool interrupted)
    {
        var requests = 0;
        var clock = new PolicyClock();
        var secondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstChunk = true;
        var body = new ResponseStream(async (buffer, cancellationToken) =>
        {
            if (interrupted)
            {
                throw new HttpIOException(HttpRequestError.ResponseEnded);
            }
            if (firstChunk)
            {
                firstChunk = false;
                clock.Advance(TimeSpan.FromSeconds(4));
                buffer.Span[0] = (byte)'{';
                return 1;
            }
            secondRead.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        });
        using var http = new HttpClient(new PlaybackHandler(() => ++requests == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }
            : new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var policy = new SpotifyRequestPolicy(timeProvider: clock);
        await using var client = new SpotifyCurrentlyPlayingClient(
            (_, _, _) => Task.FromResult(new SpotifyAccessToken("cached", clock.GetUtcNow().AddHours(1))), http, policy);
        var delay = new StepDelay();
        await using var source = CreateSource(client.GetCurrentlyPlayingAsync, delay);
        source.SetSelection(SourceDescriptor.SpotifyApi());

        if (!interrupted)
        {
            await secondRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        await WaitUntilAsync(() => delay.Requested.Count > 0 || source.GetState().Status == SourceStatus.Faulted);
        Assert.Equal(SourceStatusReason.NetworkUnavailable, source.GetState().Reason);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(delay.Requested));
        delay.Release(0);
        await WaitUntilAsync(() => source.GetState().Status == SourceStatus.Available);
        Assert.Equal(PlaybackState.Idle, (await source.ReadAsync(CancellationToken.None)).Playback);
        Assert.Equal(2, requests);
    }

    private sealed class PlaybackHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    [Fact]
    public async Task DeselectingSpotifyCancelsAnInFlightBodyWithoutRetrying()
    {
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var http = new HttpClient(new PlaybackHandler(() =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(ResponseStream.Stalled(started)) };
        }));
        await using var client = new SpotifyCurrentlyPlayingClient(
            (_, _, _) => Task.FromResult(new SpotifyAccessToken("cached", DateTimeOffset.UtcNow.AddHours(1))), http);
        var delay = new StepDelay();
        await using var source = CreateSource(client.GetCurrentlyPlayingAsync, delay);
        source.SetSelection(SourceDescriptor.SpotifyApi());
        var reading = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.SetSelection(null);

        Assert.True(reading.IsCancellationRequested);
        Assert.Equal(SourceStatus.Unconfigured, source.GetState().Status);
        await source.DisposeAsync();
        Assert.Equal(1, requests);
        Assert.Empty(delay.Requested);
    }

    [Fact]
    public async Task RestartAndReselectionWaitForThePersistedDeadlineBeforeReadingPlayback()
    {
        using var directory = new PolicyDirectory();
        var clock = new PolicyClock();
        var clientId = new SpotifyClientId("client-id");
        using var first = new SpotifyRequestPolicy(directory.Path, clock, () => 0);
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => first.SendAsync(clientId,
            SpotifyEndpoint.CurrentlyPlaying, () => Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("{\"error\":{\"reason\":\"QUOTA_EXCEEDED\"}}"),
            }), CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(4));
        var restarted = new SpotifyRequestPolicy(directory.Path, clock, () => 0);
        var delay = new StepDelay();
        var tokens = 0;
        var requests = 0;
        using var http = new HttpClient(new PlaybackHandler(() =>
        {
            Interlocked.Increment(ref requests);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        await using var client = new SpotifyCurrentlyPlayingClient((_, _, _) =>
        {
            Interlocked.Increment(ref tokens);
            return Task.FromResult(new SpotifyAccessToken("cached", clock.GetUtcNow().AddHours(1)));
        }, http, restarted);
        await using var source = new SpotifyApiSource(client.GetCurrentlyPlayingAsync, clientId, http, clock,
            delay.DelayAsync, requestPolicy: restarted);

        source.SetSelection(SourceDescriptor.SpotifyApi());
        await WaitUntilAsync(() => delay.Requested.Count == 1);
        Assert.Equal((0, 0), (tokens, requests));
        Assert.Equal(TimeSpan.FromMinutes(11), delay.Requested[0]);
        Assert.Equal(SourceStatusReason.QuotaExceeded, source.GetState().Reason);

        source.SetSelection(null);
        await delay.WaitForCancellationAsync(0);
        source.SetSelection(SourceDescriptor.SpotifyApi());
        await WaitUntilAsync(() => delay.Requested.Count == 2);
        Assert.Equal((0, 0), (tokens, requests));
        delay.Release(1);
        await WaitUntilAsync(() => delay.Requested.Count == 3);
        Assert.Equal((0, 0), (tokens, requests));
        clock.Advance(TimeSpan.FromMinutes(11));
        delay.Release(2);
        await WaitUntilAsync(() => delay.Requested.Count == 4);
        Assert.Equal((1, 1), (tokens, requests));
        Assert.Equal(TimeSpan.FromSeconds(5), delay.Requested[3]);
        Assert.Equal(SourceStatus.Available, source.GetState().Status);
    }

    [Fact]
    public async Task TokenCooldownDoesNotBlockPlaybackWithAnAlreadyUsableAccessToken()
    {
        var clock = new PolicyClock();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0);
        var clientId = new SpotifyClientId("client-id");
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(clientId,
            SpotifyEndpoint.Token, () => Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)), CancellationToken.None));
        using var http = new HttpClient(new PlaybackHandler(() => new HttpResponseMessage(HttpStatusCode.NoContent)));
        await using var client = new SpotifyCurrentlyPlayingClient(
            (_, _, _) => Task.FromResult(new SpotifyAccessToken("cached", clock.GetUtcNow().AddHours(1))),
            http, policy);
        var delay = new StepDelay();
        await using var source = new SpotifyApiSource(client.GetCurrentlyPlayingAsync, clientId, http,
            clock, delay.DelayAsync, requestPolicy: policy);
        source.SetSelection(SourceDescriptor.SpotifyApi());
        await WaitUntilAsync(() => delay.Requested.Count == 1);
        Assert.Equal(SourceStatus.Available, source.GetState().Status);
        Assert.Equal(TimeSpan.FromSeconds(5), delay.Requested[0]);
        Assert.NotNull(policy.GetCooldown(clientId, SpotifyEndpoint.Token));
    }

    [Fact]
    public async Task SelectionStartedOnUiContextDoesNotPostThePollingLoopBackToIt()
    {
        var delay = new StepDelay();
        var context = new ForwardingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        var source = CreateSource(
            (_, _) => Task.FromResult(SpotifyPlaybackResult.Idle),
            delay);
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);

            source.SetSelection(SourceDescriptor.SpotifyApi());

            Assert.Equal(0, context.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            await source.DisposeAsync();
        }
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

    private sealed class ForwardingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
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
