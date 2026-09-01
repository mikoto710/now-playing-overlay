using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.State;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Hosting;

public sealed class OverlayRuntimeTests
{
    [Fact]
    public async Task RuntimeIsOneShotAndStopAndDisposeAreIdempotent()
    {
        await using var status = new StatusFixture();
        var events = new List<string>();
        var server = new FakeHttpRuntime(events);
        var outputs = new FakeOutputRuntime(events);
        var coordinator = new FakeCoordinatorRuntime(events);
        var runtime = CreateRuntime(status, server, outputs, coordinator);

        Assert.Equal(OverlayRuntimeState.Created, runtime.State);
        await runtime.StartAsync();
        Assert.Equal(OverlayRuntimeState.Running, runtime.State);

        await runtime.StopAsync();
        await runtime.StopAsync();

        Assert.Equal(OverlayRuntimeState.Stopped, runtime.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());
        Assert.Equal(
            ["server:start", "outputs:start", "coordinator:start", "server:stop", "coordinator:dispose", "outputs:stop"],
            events);

        await runtime.DisposeAsync();
        await runtime.DisposeAsync();
        Assert.Equal(OverlayRuntimeState.Disposed, runtime.State);
        Assert.Contains("server:dispose", events);
        Assert.Contains("outputs:dispose", events);
    }

    [Fact]
    public async Task DisposeFromCreatedStillReleasesEveryOwnedComponent()
    {
        await using var status = new StatusFixture();
        var events = new List<string>();
        var runtime = CreateRuntime(
            status,
            new FakeHttpRuntime(events),
            new FakeOutputRuntime(events),
            new FakeCoordinatorRuntime(events));

        await runtime.DisposeAsync();

        Assert.Equal(OverlayRuntimeState.Disposed, runtime.State);
        Assert.Equal(
            ["coordinator:dispose", "outputs:dispose", "server:dispose"],
            events);
    }

    [Fact]
    public async Task FailedStartCleansOnlyStartedComponentsInReverseDependencyOrder()
    {
        await using var status = new StatusFixture();
        var events = new List<string>();
        var server = new FakeHttpRuntime(events);
        var outputs = new FakeOutputRuntime(events);
        var coordinator = new FakeCoordinatorRuntime(events) { ThrowOnStart = true };
        var runtime = CreateRuntime(status, server, outputs, coordinator);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());

        Assert.Equal(OverlayRuntimeState.Stopped, runtime.State);
        Assert.Equal(
            ["server:start", "outputs:start", "coordinator:start", "coordinator:dispose", "outputs:stop", "server:stop"],
            events);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeDoesNotTurnTerminalChildFailureIntoSuccessfulRetry()
    {
        await using var status = new StatusFixture();
        var events = new List<string>();
        var server = new FakeHttpRuntime(events)
        {
            OnStop = () => Task.FromException(new IOException("stop failed")),
        };
        var runtime = CreateRuntime(
            status,
            server,
            new FakeOutputRuntime(events),
            new FakeCoordinatorRuntime(events));
        await runtime.StartAsync();

        await Assert.ThrowsAsync<IOException>(() => runtime.StopAsync());
        Assert.Equal(OverlayRuntimeState.Stopped, runtime.State);

        await runtime.StopAsync();

        Assert.Equal(OverlayRuntimeState.Stopped, runtime.State);
        Assert.Equal(1, events.Count(entry => entry == "server:stop"));
        Assert.Equal(1, events.Count(entry => entry == "coordinator:dispose"));
        Assert.Equal(1, events.Count(entry => entry == "outputs:stop"));
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAfterFailedStopDisposesComponentsWithoutRepeatingStop()
    {
        await using var status = new StatusFixture();
        var events = new List<string>();
        var server = new FakeHttpRuntime(events)
        {
            OnStop = () => Task.FromException(new IOException("stop failed")),
        };
        var runtime = CreateRuntime(
            status,
            server,
            new FakeOutputRuntime(events),
            new FakeCoordinatorRuntime(events));
        await runtime.StartAsync();
        await Assert.ThrowsAsync<IOException>(() => runtime.StopAsync());

        await runtime.DisposeAsync();

        Assert.Equal(OverlayRuntimeState.Disposed, runtime.State);
        Assert.Equal(1, events.Count(entry => entry == "server:stop"));
        Assert.Contains("server:dispose", events);
    }

    [Fact]
    public async Task ConcurrentStopAndDisposeUseOneSerializedTransition()
    {
        await using var status = new StatusFixture();
        var events = new List<string>();
        var stopEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new FakeHttpRuntime(events)
        {
            OnStop = async () =>
            {
                stopEntered.TrySetResult();
                await releaseStop.Task;
            },
        };
        var runtime = CreateRuntime(
            status,
            server,
            new FakeOutputRuntime(events),
            new FakeCoordinatorRuntime(events));
        await runtime.StartAsync();

        var stop = runtime.StopAsync();
        await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OverlayRuntimeState.Stopping, runtime.State);
        var dispose = runtime.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);

        releaseStop.TrySetResult();
        await Task.WhenAll(stop, dispose);

        Assert.Equal(OverlayRuntimeState.Disposed, runtime.State);
        Assert.Equal(1, events.Count(entry => entry == "server:stop"));
    }

    private static OverlayRuntime CreateRuntime(
        StatusFixture status,
        IOverlayHttpRuntime server,
        IOutputRuntime outputs,
        INowPlayingRuntime coordinator)
    {
        return new OverlayRuntime(
            new HostOptions(),
            status.Service,
            server,
            outputs,
            coordinator,
            status.RuntimeState);
    }

    private sealed class FakeHttpRuntime(List<string> events) : IOverlayHttpRuntime
    {
        public Func<Task>? OnStop { get; init; }

        public int CurrentPort => HostOptions.DefaultPort;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            events.Add("server:start");
            return Task.CompletedTask;
        }

        public Task RebindAsync(
            int newPort,
            Action persistPort,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            events.Add("server:stop");
            if (OnStop is not null)
            {
                await OnStop();
            }
        }

        public ValueTask DisposeAsync()
        {
            events.Add("server:dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeOutputRuntime(List<string> events) : IOutputRuntime
    {
        public void Start()
        {
            events.Add("outputs:start");
        }

        public Task StopAsync()
        {
            events.Add("outputs:stop");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            events.Add("outputs:dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeCoordinatorRuntime(List<string> events) : INowPlayingRuntime
    {
        public bool ThrowOnStart { get; init; }

        public void Start()
        {
            events.Add("coordinator:start");
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("Coordinator start failed.");
            }
        }

        public ValueTask DisposeAsync()
        {
            events.Add("coordinator:dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StatusFixture : IAsyncDisposable
    {
        private readonly FakeSessionSource _source = new();
        private readonly NowPlayingCoordinator _coordinator;

        public StatusFixture()
        {
            var store = new NowPlayingStore(
                NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), DateTimeOffset.UtcNow));
            _coordinator = new NowPlayingCoordinator(_source, store, new ArtworkCache());
            RuntimeState = new HostRuntimeState(TimeProvider.System);
            Service = new HostStatusService(RuntimeState, _coordinator, _source, store);
        }

        public HostRuntimeState RuntimeState { get; }

        public HostStatusService Service { get; }

        public ValueTask DisposeAsync()
        {
            return _coordinator.DisposeAsync();
        }
    }
}
