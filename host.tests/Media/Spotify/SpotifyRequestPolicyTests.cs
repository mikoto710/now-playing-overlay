using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using NowPlayingOverlay.Host.Media.Spotify;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.Tests.Media.Spotify;

public sealed class SpotifyRequestPolicyTests
{
    private static readonly SpotifyClientId Client = new("private-client-id");

    [Theory]
    [InlineData("{\"error\":{\"reason\":\"QUOTA_EXCEEDED\"}}", true, "ok", 900)]
    [InlineData("{\"error\":{\"message\":\"Too many requests\"}}", false, "missing_reason", 30)]
    [InlineData("{\"error\":{\"reason\":\"NEW_REASON\"}}", false, "unknown_reason", 30)]
    [InlineData("broken json", false, "invalid_json", 30)]
    [InlineData("[]", false, "missing_reason", 30)]
    [InlineData("", false, "empty", 30)]
    public async Task Classifies429WithoutInventingAReasonOrLosingTheCooldown(
        string body, bool quota, string parseStatus, int seconds)
    {
        var clock = new PolicyClock();
        var log = new PolicyLogger();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0, logger: log);
        var error = await LimitAsync(policy, body);

        Assert.Equal(quota, error.Cooldown.IsQuotaExceeded);
        Assert.Equal(TimeSpan.FromSeconds(seconds), error.RetryAfter);
        Assert.Equal(clock.GetUtcNow().AddSeconds(seconds), error.Cooldown.NotBeforeUtc);
        Assert.Contains(log.Messages, line => line.Contains("BodyParseStatus=" + parseStatus));
        Assert.Contains(log.Messages, line => line.Contains(quota ? "Classification=quota" : "Classification=rate_or_unknown"));
    }

    [Fact]
    public async Task Repeated429BacksOffAndASuccessResetsOnlyItsEndpoint()
    {
        var clock = new PolicyClock();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0);
        foreach (var seconds in new[] { 30, 60, 120, 240, 300, 300 })
        {
            var error = await LimitAsync(policy, "");
            Assert.Equal(TimeSpan.FromSeconds(seconds), error.RetryAfter);
            clock.Advance(error.RetryAfter);
        }

        await LimitAsync(policy, "", SpotifyEndpoint.Token);
        using var success = await policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying,
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)), CancellationToken.None);
        Assert.NotNull(await policy.GetCooldownAsync(Client, SpotifyEndpoint.Token));
        Assert.Equal(TimeSpan.FromSeconds(30), (await LimitAsync(policy, "")).RetryAfter);
    }

    [Fact]
    public async Task QuotaProbesIncreaseToOneHour()
    {
        var clock = new PolicyClock();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0);
        foreach (var minutes in new[] { 15, 30, 60, 60 })
        {
            var error = await LimitAsync(policy, "{\"error\":{\"reason\":\"QUOTA_EXCEEDED\"}}");
            Assert.Equal(TimeSpan.FromMinutes(minutes), error.RetryAfter);
            clock.Advance(error.RetryAfter);
        }
    }

    [Fact]
    public async Task ANewUnconfirmed429DoesNotInheritThePreviousQuotaClassification()
    {
        var clock = new PolicyClock();
        using var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0);
        var quota = await LimitAsync(policy, "{\"error\":{\"reason\":\"QUOTA_EXCEEDED\"}}");
        clock.Advance(quota.RetryAfter);
        var unconfirmed = await LimitAsync(policy, "");
        Assert.False(unconfirmed.Cooldown.IsQuotaExceeded);
        Assert.Equal(TimeSpan.FromSeconds(60), unconfirmed.RetryAfter);
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{\"Version\":2,\"Entries\":[]}")]
    [InlineData("{\"Version\":1,\"Entries\":null}")]
    [InlineData("{\"Version\":1,\"Entries\":[null]}")]
    [InlineData("{\"Version\":1,\"Entries\":[{\"ClientKey\":\"invalid\"}]}")]
    public async Task CorruptStorageIsDiagnosedWithoutPreventingSuccessfulRequests(string body)
    {
        using var directory = new PolicyDirectory();
        File.WriteAllText(directory.Path, body);
        var log = new PolicyLogger();
        using var policy = new SpotifyRequestPolicy(directory.Path, logger: log);
        using var response = await policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying,
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)), CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, response.Message.StatusCode);
        Assert.Contains(log.Messages, line => line.Contains("load failed"));
    }

    [Theory]
    [InlineData("invalid header")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("Mon, 19 Oct 2026 00:00:00 GMT")]
    public async Task InvalidOrPastRetryAfterStillHasAMinimumCooldown(string header)
    {
        using var policy = new SpotifyRequestPolicy(timeProvider: new PolicyClock(), jitter: () => 0);
        var response = Response("");
        response.Headers.TryAddWithoutValidation("Retry-After", header);
        var error = await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(Client,
            SpotifyEndpoint.CurrentlyPlaying, () => Task.FromResult(response), CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Fact]
    public async Task ConcurrentSendCannotSlipPastA429AdmissionCheck()
    {
        using var policy = new SpotifyRequestPolicy(timeProvider: new PolicyClock(), jitter: () => 0);
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var first = policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying,
            () => { calls++; return reply.Task; }, CancellationToken.None);
        var second = policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying,
            () => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }, CancellationToken.None);
        Assert.False(second.IsCompleted);
        reply.SetResult(Response(""));
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => first);
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => second);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerDelayCanExceedTheLocalCapAndDoesNotPermitEarlyRequests(bool quota)
    {
        var clock = new PolicyClock();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0);
        var calls = 0;
        var response = Response(quota ? "{\"error\":{\"reason\":\"QUOTA_EXCEEDED\"}}" : "");
        response.Headers.RetryAfter = new(TimeSpan.FromHours(8));
        var error = await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(
            Client, SpotifyEndpoint.CurrentlyPlaying, () => { calls++; return Task.FromResult(response); }, CancellationToken.None));
        Assert.Equal(TimeSpan.FromHours(8), error.RetryAfter);
        clock.Advance(TimeSpan.FromHours(8) - TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(
            Client, SpotifyEndpoint.CurrentlyPlaying,
            () => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Slow429BodyPreservesTheAbsoluteServerDeadlineAndAddsLocalJitter()
    {
        var clock = new PolicyClock();
        using var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 1);
        var serverDeadline = clock.GetUtcNow().AddHours(1);
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StreamContent(new ResponseStream((_, _) =>
            {
                clock.Advance(TimeSpan.FromSeconds(4));
                return ValueTask.FromResult(0);
            })),
        };
        response.Headers.RetryAfter = new(serverDeadline);
        var error = await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(
            Client, SpotifyEndpoint.CurrentlyPlaying, () => Task.FromResult(response), CancellationToken.None));
        Assert.Equal(serverDeadline.AddSeconds(3), error.Cooldown.NotBeforeUtc);
        Assert.Equal(TimeSpan.FromSeconds(3599), error.RetryAfter);
    }

    [Fact]
    public async Task OversizedAndUnreadableBodiesStillRetain429AndRetryAfter()
    {
        foreach (var (content, status) in new (HttpContent, string)[]
        {
            (new StringContent(new string('x', 16385)), "too_large"),
            (new StreamContent(new BrokenStream()), "read_failed"),
        })
        {
            var log = new PolicyLogger();
            var policy = new SpotifyRequestPolicy(timeProvider: new PolicyClock(), jitter: () => 0, logger: log);
            var response = Response("");
            response.Content = content;
            response.Headers.RetryAfter = new(TimeSpan.FromMinutes(2));
            var error = await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(
                Client, SpotifyEndpoint.CurrentlyPlaying, () => Task.FromResult(response), CancellationToken.None));
            Assert.Equal(TimeSpan.FromMinutes(2), error.RetryAfter);
            Assert.NotNull(await policy.GetCooldownAsync(Client, SpotifyEndpoint.CurrentlyPlaying));
            Assert.Contains(log.Messages, line => line.Contains("BodyParseStatus=" + status));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RecreatingThePolicyPreservesTheRemainingDeadlineAndFailureCount(int endpointValue)
    {
        var endpoint = (SpotifyEndpoint)endpointValue;
        using var directory = new PolicyDirectory();
        var clock = new PolicyClock();
        var first = new SpotifyRequestPolicy(directory.Path, clock, () => 0);
        await LimitAsync(first, "{\"error\":{\"reason\":\"QUOTA_EXCEEDED\"}}", endpoint);
        clock.Advance(TimeSpan.FromMinutes(4));

        var log = new PolicyLogger();
        using var restarted = new SpotifyRequestPolicy(directory.Path, clock, () => 0, log);
        Assert.Equal(TimeSpan.FromMinutes(11), (await restarted.GetCooldownAsync(Client, endpoint))!.RetryAfter);
        Assert.Contains(log.Messages, line => line.Contains("restored") && line.Contains("RemainingSeconds=660"));
        Assert.True((await restarted.GetCooldownAsync(Client, endpoint))!.Cooldown.IsQuotaExceeded);
        var calls = 0;
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => restarted.SendAsync(Client, endpoint,
            () => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }, CancellationToken.None));
        Assert.Equal(0, calls);
        clock.Advance(TimeSpan.FromMinutes(11));
        var repeated = await LimitAsync(restarted, "{\"error\":{\"reason\":\"QUOTA_EXCEEDED\"}}", endpoint);
        Assert.Equal(2, repeated.Cooldown.Consecutive429);
        Assert.Equal(TimeSpan.FromMinutes(30), repeated.RetryAfter);
        Assert.DoesNotContain(Client.Value, File.ReadAllText(directory.Path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationOrTimeoutAfter429HeadersStillPersistsTheServerDeadline(bool callerCancels)
    {
        using var directory = new PolicyDirectory();
        var clock = new PolicyClock();
        using var policy = new SpotifyRequestPolicy(directory.Path, clock, () => 0);
        using var caller = new CancellationTokenSource();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StreamContent(ResponseStream.Stalled(started)),
        };
        response.Headers.RetryAfter = new(TimeSpan.FromMinutes(2));
        var request = policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying, () => Task.FromResult(response), caller.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (callerCancels)
        {
            caller.Cancel();
        }
        else
        {
            var deadline = Assert.Single(clock.Timers);
            Assert.Equal(TimeSpan.FromSeconds(5), deadline.DueTime);
            clock.Advance(deadline.DueTime);
            deadline.Fire();
        }
        var limited = await Assert.ThrowsAsync<SpotifyRateLimitException>(() => request);
        Assert.False(limited.Cooldown.IsQuotaExceeded);
        using var restarted = new SpotifyRequestPolicy(directory.Path, clock);
        Assert.Equal(TimeSpan.FromMinutes(2), (await restarted.GetCooldownAsync(Client, SpotifyEndpoint.CurrentlyPlaying))!.RetryAfter);
    }

    [Fact]
    public async Task RestartAfterAClockRollbackStillHonorsBothEndpointDeadlines()
    {
        using var directory = new PolicyDirectory();
        var clock = new PolicyClock();
        using (var original = new SpotifyRequestPolicy(directory.Path, clock, () => 0))
        {
            await LimitAsync(original, "");
            await LimitAsync(original, """{"error":{"reason":"QUOTA_EXCEEDED"}}""", SpotifyEndpoint.Token);
            clock.Advance(TimeSpan.FromSeconds(30));
            clock.UtcNow -= TimeSpan.FromHours(1);
            await LimitAsync(original, "");
        }

        var restartedClock = new PolicyClock { UtcNow = clock.GetUtcNow() };
        using var restarted = new SpotifyRequestPolicy(directory.Path, restartedClock, () => 0);
        var sends = 0;
        Task<HttpResponseMessage> Send()
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        var blocked = await Assert.ThrowsAsync<SpotifyRateLimitException>(() =>
            restarted.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying, Send, CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(60), blocked.RetryAfter);
        Assert.Equal(2, blocked.Cooldown.Consecutive429);
        Assert.Equal(0, sends);
        restartedClock.Advance(blocked.RetryAfter);
        using var success = await restarted.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying, Send, CancellationToken.None);
        Assert.Equal(1, sends);
        Assert.True((await restarted.GetCooldownAsync(Client, SpotifyEndpoint.Token))!.Cooldown.IsQuotaExceeded);
    }

    [Fact]
    public async Task SuccessClearsPersistedCooldownAndLogsRecovery()
    {
        using var directory = new PolicyDirectory();
        var clock = new PolicyClock();
        var log = new PolicyLogger();
        var policy = new SpotifyRequestPolicy(directory.Path, clock, () => 0, log);
        await LimitAsync(policy, "");
        policy.SetPollingState("rate-cooldown");
        clock.Advance(TimeSpan.FromSeconds(30));
        using var success = await policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying,
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)), CancellationToken.None);
        policy.SetPollingState("playing:3s", TimeSpan.FromSeconds(3));
        using var restarted = new SpotifyRequestPolicy(directory.Path, clock);
        Assert.Null(await restarted.GetCooldownAsync(Client, SpotifyEndpoint.CurrentlyPlaying));
        Assert.Contains(log.Messages, line => line.Contains("recovered") && line.Contains("Consecutive429=1"));
        Assert.Contains(log.Messages, line => line.Contains("polling resumed") && line.Contains("IntervalSeconds=3"));
    }

    [Fact]
    public async Task WallClockChangesDoNotShortenAnActiveCooldown()
    {
        var clock = new PolicyClock();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0);
        await LimitAsync(policy, "");
        clock.UtcNow += TimeSpan.FromDays(1);
        Assert.Equal(TimeSpan.FromSeconds(30), (await policy.GetCooldownAsync(Client, SpotifyEndpoint.CurrentlyPlaying))!.RetryAfter);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(await policy.GetCooldownAsync(Client, SpotifyEndpoint.CurrentlyPlaying));
    }

    [Fact]
    public async Task StorageFailureKeepsTheInMemoryCooldownAndDiagnosticsContainOnlyAllowedFields()
    {
        using var directory = new PolicyDirectory();
        Directory.CreateDirectory(directory.Path);
        var log = new PolicyLogger();
        var policy = new SpotifyRequestPolicy(directory.Path, new PolicyClock(), () => 0, log);
        await LimitAsync(policy, "{\"error\":{\"reason\":\"SECRET_TOKEN\",\"message\":\"private song\"},\"access_token\":\"credential\"}");
        Assert.NotNull(await policy.GetCooldownAsync(Client, SpotifyEndpoint.CurrentlyPlaying));
        Assert.Contains(log.Messages, line => line.Contains("persist"));
        var messages = string.Join('\n', log.Messages);
        foreach (var secret in new[] { "SECRET_TOKEN", "private song", "credential", Client.Value })
        {
            Assert.DoesNotContain(secret, messages);
        }
    }

    [Fact]
    public async Task PeriodicSummariesCountEachEndpointResetOnEveryTickAndStopOnDisposal()
    {
        var clock = new PolicyClock();
        var log = new PolicyLogger();
        using var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0, logger: log);
        var timer = Assert.Single(clock.Timers);
        Assert.Equal(TimeSpan.FromMinutes(15), timer.DueTime);
        Assert.Equal(TimeSpan.FromMinutes(15), timer.Period);

        using var success = await policy.SendAsync(Client, SpotifyEndpoint.Token,
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)), CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StreamContent(new ResponseStream((_, _) =>
            {
                // The 429 headers and the end of its body fall in different summary windows.
                clock.Advance(TimeSpan.FromMilliseconds(1100));
                timer.Fire();
                return ValueTask.FromResult(0);
            })),
        };
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying,
            () => Task.FromResult(response), CancellationToken.None));
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying,
            () => throw new InvalidOperationException("Locally blocked requests must not send."), CancellationToken.None));
        Assert.Contains(log.Messages, line => line.Contains("Endpoint=currently-playing Requests=1 Successes=0 Responses429=1"));
        Assert.Contains(log.Messages, line => line.Contains("Endpoint=token Requests=1 Successes=1 Responses429=0"));

        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromMilliseconds(100));
        timer.Fire();
        Assert.Contains(log.Messages, line => line.Contains("Endpoint=currently-playing Requests=0 Successes=0 Responses429=0"));
        Assert.Contains(log.Messages, line => line.Contains("Endpoint=token Requests=0 Successes=0 Responses429=0"));
        Assert.Equal(4, log.Messages.Count(line => line.Contains("summary")));

        policy.Dispose();
        Assert.True(timer.IsDisposed);
        timer.Fire(); // A callback already queued before disposal can still run.
        Assert.Equal(4, log.Messages.Count(line => line.Contains("summary")));
    }

    private static Task<SpotifyRateLimitException> LimitAsync(SpotifyRequestPolicy policy, string body,
        SpotifyEndpoint endpoint = SpotifyEndpoint.CurrentlyPlaying) =>
        Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(
            Client, endpoint, () => Task.FromResult(Response(body)), CancellationToken.None));

    private static HttpResponseMessage Response(string body) => new((HttpStatusCode)429)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("private network error"));
    }
}

internal sealed class PolicyClock : TimeProvider
{
    private long _timestamp;
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    public List<RecordedTimer> Timers { get; } = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => UtcNow;
    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan duration)
    {
        UtcNow += duration;
        _timestamp += duration.Ticks;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new RecordedTimer(callback, state, dueTime, period);
        Timers.Add(timer);
        return timer;
    }

    public sealed class RecordedTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
    {
        public TimeSpan DueTime { get; } = dueTime;
        public TimeSpan Period { get; } = period;
        public bool IsDisposed { get; private set; }

        public void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => IsDisposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class PolicyDirectory : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spotify-policy-" + Guid.NewGuid().ToString("N"));
    public string Path => System.IO.Path.Combine(_directory, "cooldowns.json");
    public PolicyDirectory() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

internal sealed class PolicyLogger : ILogger<SpotifyRequestPolicy>
{
    private readonly object _gate = new();
    private readonly List<string> _messages = [];
    public IReadOnlyList<string> Messages { get { lock (_gate) { return _messages.ToArray(); } } }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_gate) { _messages.Add(formatter(state, exception)); }
    }
}
