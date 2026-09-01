using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.WindowTitles;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Outputs;
using NowPlayingOverlay.Host.State;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging;

namespace NowPlayingOverlay.Host.Tests.Media.WindowTitles;

public sealed class WindowTitleSourceTests
{
    [Fact]
    public void Win32CatalogCanEnumerateTheCurrentDesktop()
    {
        var windows = new Win32WindowTitleCatalog().GetWindows();

        Assert.NotNull(windows);
    }

    [Fact]
    public async Task SelectedWindowPublishesExplicitlyParsedMetadata()
    {
        var target = Target();
        var catalog = new FakeCatalog(new WindowTitleWindow(target, "Artist - Song"));
        await using var source = CreateSource(catalog, target, split: true);
        source.SetSelection(Descriptor(target));

        var observation = await source.ReadAsync(CancellationToken.None);

        Assert.Equal(SourceProvider.WindowTitle, source.Provider);
        Assert.Equal(PlaybackState.Playing, observation.Playback);
        Assert.Equal("Song", observation.Track!.Title);
        Assert.Equal("Artist", observation.Track.Artist);
        Assert.Null(observation.Timeline);
        Assert.Null(observation.ArtworkReader);
        Assert.Equal(SourceStatus.Available, source.GetState().Status);
    }

    [Fact]
    public async Task MissingSeparatorProducesIdleInsteadOfMislabelingTheApplication()
    {
        var target = Target();
        var catalog = new FakeCatalog(new WindowTitleWindow(target, "Player"));
        await using var source = CreateSource(catalog, target, split: true);
        source.SetSelection(Descriptor(target));

        var observation = await source.ReadAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Idle, observation.Playback);
        Assert.Null(observation.Track);
        Assert.Equal(SourceStatus.Available, source.GetState().Status);
    }

    [Fact]
    public async Task MissingAndAmbiguousTargetsAreExplicitlyUnavailable()
    {
        var target = Target();
        var catalog = new FakeCatalog();
        await using var source = CreateSource(catalog, target, split: false);
        source.SetSelection(Descriptor(target));

        var missing = await source.ReadAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Unavailable, missing.Playback);
        Assert.Equal(SourceStatusReason.Missing, source.GetState().Reason);

        catalog.SetWindows(
            new WindowTitleWindow(target, "First"),
            new WindowTitleWindow(target, "Second"));
        var ambiguous = await source.ReadAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Unavailable, ambiguous.Playback);
        Assert.Equal(SourceStatusReason.Ambiguous, source.GetState().Reason);
    }

    [Fact]
    public async Task DiscoveryGroupsRestartVolatileWindowsByStableTarget()
    {
        var target = Target();
        var other = Target(processName: "OtherPlayer", windowClass: "OtherWindow");
        var catalog = new FakeCatalog(
            new WindowTitleWindow(target, "First"),
            new WindowTitleWindow(target, "Second"),
            new WindowTitleWindow(other, "Other"));
        await using var source = CreateSource(catalog, target, split: false);

        var discovery = await source.RefreshSourcesAsync();

        Assert.Equal(2, discovery.Candidates.Count);
        var ambiguous = Assert.Single(
            discovery.Candidates,
            candidate => candidate.Target.InstanceId == target.InstanceId);
        Assert.Equal(2, ambiguous.MatchCount);
        Assert.Equal(string.Empty, ambiguous.CurrentTitle);
    }

    [Fact]
    public async Task PollingSignalsOnlyAfterTheEffectiveTitleChanges()
    {
        var target = Target();
        var catalog = new FakeCatalog(new WindowTitleWindow(target, "First"));
        await using var source = CreateSource(
            catalog,
            target,
            split: false,
            pollInterval: TimeSpan.FromMilliseconds(20));
        source.SetSelection(Descriptor(target));
        _ = await source.ReadAsync(CancellationToken.None);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Changed += (_, _) => changed.TrySetResult();

        await Task.Delay(60);
        Assert.False(changed.Task.IsCompleted);
        catalog.SetWindows(new WindowTitleWindow(target, "Second"));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var observation = await source.ReadAsync(CancellationToken.None);
        Assert.Equal("Second", observation.Track!.Title);
    }

    [Fact]
    public async Task PollingRebindsTheStableTargetAfterThePlayerRestarts()
    {
        var target = Target();
        var catalog = new FakeCatalog();
        await using var source = CreateSource(
            catalog,
            target,
            split: false,
            pollInterval: TimeSpan.FromMilliseconds(20));
        source.SetSelection(Descriptor(target));
        var missing = await source.ReadAsync(CancellationToken.None);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Changed += (_, _) => changed.TrySetResult();

        catalog.SetWindows(new WindowTitleWindow(target, "Restarted song"));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var rebound = await source.ReadAsync(CancellationToken.None);

        Assert.Equal(PlaybackState.Unavailable, missing.Playback);
        Assert.Equal(PlaybackState.Playing, rebound.Playback);
        Assert.Equal("Restarted song", rebound.Track!.Title);
        Assert.Equal(SourceStatus.Available, source.GetState().Status);
    }

    [Fact]
    public async Task SelectionStartedOnUiContextDoesNotPostThePollingLoopBackToIt()
    {
        var target = Target();
        var context = new ForwardingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        var source = CreateSource(
            new FakeCatalog(new WindowTitleWindow(target, "Song")),
            target,
            split: false,
            pollInterval: TimeSpan.FromMinutes(1));
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);

            source.SetSelection(Descriptor(target));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            await source.DisposeAsync();
        }

        Assert.Equal(0, context.PostCount);
    }

    [Fact]
    public async Task ParsedWindowTitleFlowsThroughTheExistingTextOutput()
    {
        using var directory = new TemporaryDirectory();
        var textPath = Path.Combine(directory.Path, "window-title.txt");
        var target = Target();
        var catalog = new FakeCatalog(new WindowTitleWindow(target, "Artist - Song"));
        await using var source = CreateSource(catalog, target, split: true);
        source.SetSelection(Descriptor(target));
        var store = new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), DateTimeOffset.UtcNow));
        await using var output = new OutputManager(
            store,
            new ArtworkCache(),
            new OutputSettings
            {
                Text = new TextOutputSettings
                {
                    Enabled = true,
                    FilePath = textPath,
                    Template = "{title}{newline}{artist}",
                },
            });
        await using var coordinator = new NowPlayingCoordinator(
            source,
            store,
            new ArtworkCache(),
            new NowPlayingCoordinatorOptions { DebounceDelay = TimeSpan.Zero });

        output.Start();
        coordinator.Start();
        await WaitForAsync(() => File.Exists(textPath)
            && File.ReadAllText(textPath) == $"Song{Environment.NewLine}Artist");

        Assert.Equal(
            $"Song{Environment.NewLine}Artist",
            await File.ReadAllTextAsync(textPath));
    }

    [Fact]
    public async Task DiscoveryFailureIsReportedWithoutThrowingFromSettingsRefresh()
    {
        var target = Target();
        var catalog = new FakeCatalog { Error = new System.ComponentModel.Win32Exception(5) };
        await using var source = CreateSource(catalog, target, split: false);
        source.SetSelection(Descriptor(target));

        var discovery = await source.RefreshSourcesAsync();
        var observation = await source.ReadAsync(CancellationToken.None);

        Assert.Empty(discovery.Candidates);
        Assert.Equal(SourceStatus.Faulted, discovery.State.Status);
        Assert.Equal(PlaybackState.Unavailable, observation.Playback);
        Assert.Equal(SourceStatus.Faulted, source.GetState().Status);
    }

    [Fact]
    public async Task CatalogFaultWritesOneSanitizedLogWithoutTitleOrExecutablePath()
    {
        var target = Target();
        const string sensitiveText = @"Secret Song C:\Private\Player.exe";
        var catalog = new FakeCatalog
        {
            Error = new System.ComponentModel.Win32Exception(5, sensitiveText),
        };
        var logger = new CapturingLogger<WindowTitleSource>();
        await using var source = new WindowTitleSource(
            catalog,
            new WindowTitleSettings { Target = target },
            logger: logger);
        source.SetSelection(Descriptor(target));

        _ = await source.RefreshSourcesAsync();
        _ = await source.ReadAsync(CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("Win32Exception", entry, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveText, entry, StringComparison.Ordinal);
        Assert.DoesNotContain(target.ExecutablePath!, entry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulDiscoveryResetsFaultLogDeduplication()
    {
        var target = Target();
        var catalog = new FakeCatalog
        {
            Error = new System.ComponentModel.Win32Exception(5, "first failure"),
        };
        var logger = new CapturingLogger<WindowTitleSource>();
        await using var source = new WindowTitleSource(
            catalog,
            new WindowTitleSettings { Target = target },
            logger: logger);

        _ = await source.RefreshSourcesAsync();
        catalog.Error = null;
        catalog.SetWindows(new WindowTitleWindow(target, "Recovered"));
        _ = await source.RefreshSourcesAsync();
        catalog.Error = new System.ComponentModel.Win32Exception(5, "second failure");
        _ = await source.RefreshSourcesAsync();

        Assert.Equal(2, logger.Entries.Count);
    }

    private static WindowTitleSource CreateSource(
        FakeCatalog catalog,
        WindowTitleTargetSettings target,
        bool split,
        TimeSpan? pollInterval = null)
    {
        return new WindowTitleSource(
            catalog,
            new WindowTitleSettings
            {
                Target = target,
                ParseMode = split ? WindowTitleParseMode.Split : WindowTitleParseMode.WholeTitle,
                Separator = " - ",
                LeftField = WindowTitleField.Artist,
            },
            pollInterval);
    }

    private static WindowTitleTargetSettings Target(
        string processName = "Player",
        string windowClass = "PlayerWindow")
    {
        return new WindowTitleTargetSettings
        {
            ProcessName = processName,
            ExecutablePath = $@"C:\Apps\{processName}.exe",
            WindowClass = windowClass,
        };
    }

    private static SourceDescriptor Descriptor(WindowTitleTargetSettings target)
    {
        return SourceDescriptor.WindowTitle(target.InstanceId, target.DisplayName);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class FakeCatalog(params WindowTitleWindow[] windows) : IWindowTitleCatalog
    {
        private readonly object _gate = new();
        private IReadOnlyList<WindowTitleWindow> _windows = windows;

        public Exception? Error { get; set; }

        public IReadOnlyList<WindowTitleWindow> GetWindows()
        {
            lock (_gate)
            {
                if (Error is not null)
                {
                    throw Error;
                }

                return _windows.ToArray();
            }
        }

        public void SetWindows(params WindowTitleWindow[] windows)
        {
            lock (_gate)
            {
                _windows = windows;
            }
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Entries.Add(formatter(state, exception));
            }
        }
    }
}
