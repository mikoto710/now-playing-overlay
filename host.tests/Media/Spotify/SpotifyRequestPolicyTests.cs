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
        Assert.Equal(SpotifyEndpoint.Token, policy.GetCooldown(Client)!.Cooldown.Endpoint);
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
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
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
    public async Task HttpDateAndPositiveJitterAreAddedWithoutShorteningTheServerDeadline()
    {
        var clock = new PolicyClock();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 1);
        var response = Response("");
        response.Headers.RetryAfter = new(clock.GetUtcNow().AddHours(1));
        var error = await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(
            Client, SpotifyEndpoint.CurrentlyPlaying, () => Task.FromResult(response), CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(3603), error.RetryAfter);
    }

    [Fact]
    public async Task OversizedAndUnreadableBodiesStillRetain429AndRetryAfter()
    {
        foreach (var content in new HttpContent[]
        {
            new StringContent(new string('x', 16385)),
            new StreamContent(new BrokenStream()),
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
            Assert.NotNull(policy.GetCooldown(Client));
            Assert.Contains(log.Messages, line => line.Contains("BodyParseStatus=too_large") || line.Contains("BodyParseStatus=read_failed"));
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
        Assert.Equal(TimeSpan.FromMinutes(11), restarted.GetCooldown(Client)!.RetryAfter);
        Assert.Contains(log.Messages, line => line.Contains("restored") && line.Contains("RemainingSeconds=660"));
        Assert.True(restarted.GetCooldown(Client)!.Cooldown.IsQuotaExceeded);
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
        Assert.True(restarted.GetCooldown(Client, SpotifyEndpoint.Token)!.Cooldown.IsQuotaExceeded);
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
        policy.SetPollingState("playing:5s", TimeSpan.FromSeconds(5));
        Assert.Null(new SpotifyRequestPolicy(directory.Path, clock).GetCooldown(Client));
        Assert.Contains(log.Messages, line => line.Contains("recovered") && line.Contains("Consecutive429=1"));
        Assert.Contains(log.Messages, line => line.Contains("polling resumed") && line.Contains("IntervalSeconds=5"));
    }

    [Fact]
    public async Task WallClockChangesDoNotShortenAnActiveCooldown()
    {
        var clock = new PolicyClock();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0);
        await LimitAsync(policy, "");
        clock.UtcNow += TimeSpan.FromDays(1);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetCooldown(Client)!.RetryAfter);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(policy.GetCooldown(Client));
    }

    [Fact]
    public async Task StorageFailureKeepsTheInMemoryCooldownAndDiagnosticsContainOnlyAllowedFields()
    {
        using var directory = new PolicyDirectory();
        Directory.CreateDirectory(directory.Path);
        var log = new PolicyLogger();
        var policy = new SpotifyRequestPolicy(directory.Path, new PolicyClock(), () => 0, log);
        await LimitAsync(policy, "{\"error\":{\"reason\":\"SECRET_TOKEN\",\"message\":\"private song\"},\"access_token\":\"credential\"}");
        Assert.NotNull(policy.GetCooldown(Client));
        Assert.Contains(log.Messages, line => line.Contains("persist"));
        var messages = string.Join('\n', log.Messages);
        foreach (var secret in new[] { "SECRET_TOKEN", "private song", "credential", Client.Value })
        {
            Assert.DoesNotContain(secret, messages);
        }
    }

    [Fact]
    public async Task PeriodicSummaryCountsActualSendsAndBlockedRequestsDoNotInflateIt()
    {
        var clock = new PolicyClock();
        var log = new PolicyLogger();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0, logger: log);
        await LimitAsync(policy, "");
        await Assert.ThrowsAsync<SpotifyRateLimitException>(() => policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying,
            () => throw new InvalidOperationException("must not send"), CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(15));
        using var success = await policy.SendAsync(Client, SpotifyEndpoint.CurrentlyPlaying,
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)), CancellationToken.None);
        Assert.Contains(log.Messages, line => line.Contains("summary") && line.Contains("Requests=1")
            && line.Contains("Successes=0") && line.Contains("Responses429=1"));
    }

    [Fact]
    public async Task PeriodicSummaryRunsWithoutAnotherRequestAndStopsOnDisposal()
    {
        var clock = new PolicyClock();
        var log = new PolicyLogger();
        var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0, logger: log);
        await LimitAsync(policy, "");
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Single(log.Messages, line => line.Contains("summary"));
        policy.Dispose();
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Single(log.Messages, line => line.Contains("summary"));
    }

    [Fact]
    public async Task DelayedSummaryCallbackDoesNotSkipTheNextScheduledWindow()
    {
        var clock = new PolicyClock();
        var log = new PolicyLogger();
        using var policy = new SpotifyRequestPolicy(timeProvider: clock, jitter: () => 0, logger: log);
        await LimitAsync(policy, "");
        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromMilliseconds(100));
        Assert.Single(log.Messages, line => line.Contains("summary"));
        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, log.Messages.Count(line => line.Contains("summary")));
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
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private long _timestamp;
    private readonly List<PolicyTimer> _timers = [];
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => UtcNow;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan duration)
    {
        UtcNow += duration;
        _timestamp += duration.Ticks;
        foreach (var timer in _timers.ToArray()) { timer.FireIfDue(); }
    }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new PolicyTimer(this, callback, state, dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    private sealed class PolicyTimer : ITimer
    {
        private readonly PolicyClock _clock;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private long _next;
        private TimeSpan _period;
        private bool _disposed;
        public PolicyTimer(PolicyClock clock, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _clock = clock; _callback = callback; _state = state;
            Change(dueTime, period);
        }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _next = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : _clock.GetTimestamp() + dueTime.Ticks;
            _period = period;
            return !_disposed;
        }
        public void FireIfDue()
        {
            if (!_disposed && _clock.GetTimestamp() >= _next)
            {
                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _next = long.MaxValue;
                }
                else
                {
                    do { _next += _period.Ticks; } while (_next <= _clock.GetTimestamp());
                }
                _callback(_state);
            }
        }
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
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
