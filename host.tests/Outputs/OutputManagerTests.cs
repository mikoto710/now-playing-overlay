using System.Text.Json;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Outputs;
using NowPlayingOverlay.Host.Protocol;
using NowPlayingOverlay.Host.State;
using NowPlayingOverlay.Host.Tests.TestInfrastructure;

namespace NowPlayingOverlay.Host.Tests.Outputs;

public sealed class OutputManagerTests
{
    [Fact]
    public async Task WritesMultipleTextTargetsAndExactProtocolV3Json()
    {
        using var directory = new TemporaryDirectory();
        var titlePath = Path.Combine(directory.Path, "title.txt");
        var nowPlayingPath = Path.Combine(directory.Path, "now-playing.txt");
        var jsonPath = Path.Combine(directory.Path, "state.json");
        var store = CreateStore();
        var manager = new OutputManager(
            store,
            new ArtworkCache(),
            new OutputSettings
            {
                Text =
                [
                    new TextOutputSettings
                    {
                        Enabled = true,
                        Name = "Title",
                        FilePath = titlePath,
                        Template = "{title}",
                    },
                    new TextOutputSettings
                    {
                        Enabled = true,
                        Name = "Now Playing",
                        FilePath = nowPlayingPath,
                        Template = "{nowPlaying}",
                    },
                ],
                Json = new JsonOutputSettings
                {
                    Enabled = true,
                    FilePath = jsonPath,
                    Format = JsonOutputFormat.Indented,
                },
            });
        manager.Start();
        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("Title", "Artist", "Album"),
            artwork: null,
            DateTimeOffset.UtcNow,
            out var committed);

        await WaitForAsync(() => File.Exists(jsonPath)
            && File.Exists(titlePath)
            && File.Exists(nowPlayingPath));

        Assert.Equal("Title", await File.ReadAllTextAsync(titlePath));
        Assert.Equal("Artist - Title", await File.ReadAllTextAsync(nowPlayingPath));
        var json = await File.ReadAllTextAsync(jsonPath);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(3, document.RootElement.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(
            committed.SnapshotRevision,
            document.RootElement.GetProperty("snapshotRevision").GetInt64());
        Assert.Equal(
            "Title",
            document.RootElement.GetProperty("track").GetProperty("title").GetString());
        Assert.Contains('\n', await File.ReadAllTextAsync(jsonPath));
        Assert.Equal(
            ProtocolJson.Serialize(NowPlayingStateMapper.Map(committed), indented: true),
            json);
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task NoMediaPoliciesClearPlaceholderOrKeepTheirOwnTargets()
    {
        using var directory = new TemporaryDirectory();
        var clearPath = Path.Combine(directory.Path, "clear.txt");
        var placeholderPath = Path.Combine(directory.Path, "placeholder.txt");
        var keepPath = Path.Combine(directory.Path, "keep.txt");
        var store = CreateStore();
        await using var manager = new OutputManager(
            store,
            new ArtworkCache(),
            new OutputSettings
            {
                Text =
                [
                    CreateTextOutput("Clear", clearPath, NoMediaOutputBehavior.Clear),
                    CreateTextOutput(
                        "Placeholder",
                        placeholderPath,
                        NoMediaOutputBehavior.Placeholder,
                        "Nothing playing"),
                    CreateTextOutput("Keep", keepPath, NoMediaOutputBehavior.KeepLast),
                ],
            });
        manager.Start();
        var source = SourceDescriptor.WindowsMedia("Player.App");
        store.TryCommit(
            source,
            PlaybackState.Playing,
            TrackMetadata.Create("Title", "Artist", null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);
        await WaitForAsync(() => File.Exists(clearPath)
            && File.Exists(placeholderPath)
            && File.Exists(keepPath));
        store.TryCommit(
            source,
            PlaybackState.Idle,
            track: null,
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);
        await WaitForAsync(() => File.ReadAllText(clearPath).Length == 0
            && File.ReadAllText(placeholderPath) == "Nothing playing");

        Assert.Equal(string.Empty, await File.ReadAllTextAsync(clearPath));
        Assert.Equal("Nothing playing", await File.ReadAllTextAsync(placeholderPath));
        Assert.Equal("Artist - Title", await File.ReadAllTextAsync(keepPath));
    }

    [Fact]
    public async Task ArtworkUsesExactCacheEntryAndWritesStablePngTarget()
    {
        using var directory = new TemporaryDirectory();
        var artworkPath = Path.Combine(directory.Path, "cover.png");
        var cache = new ArtworkCache();
        var jpeg = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAgAAAQABAAD//gAQTGF2YzYyLjExLjEwMAD/2wBDAAgEBAQEBAUFBQUFBQYGBgYGBgYGBgYGBgYHBwcICAgHBwcGBgcHCAgICAkJCQgICAgJCQoKCgwMCwsODg4RERT/xABLAAEBAAAAAAAAAAAAAAAAAAAABgEBAAAAAAAAAAAAAAAAAAAABhABAAAAAAAAAAAAAAAAAAAAABEBAAAAAAAAAAAAAAAAAAAAAP/AABEIAAIAAgMBIgACEQADEQD/2gAMAwEAAhEDEQA/AIsAUCX/2Q==");
        Assert.True(cache.TryAdd(ArtworkPayload.Create(jpeg), out var entry));
        var store = CreateStore();
        await using var manager = new OutputManager(
            store,
            cache,
            new OutputSettings
            {
                Artwork = new ArtworkOutputSettings
                {
                    Enabled = true,
                    FilePath = artworkPath,
                    MissingArtworkBehavior = MissingArtworkBehavior.Delete,
                },
            });
        manager.Start();
        var source = SourceDescriptor.WindowsMedia("Player.App");
        var track = TrackMetadata.Create("Title", "Artist", null);
        store.TryCommit(
            source,
            PlaybackState.Playing,
            track,
            new ArtworkDescriptor(
                ArtworkRevision: 1,
                entry!.ArtworkId,
                entry.ContentType,
                entry.ByteLength),
            DateTimeOffset.UtcNow,
            out _);
        await WaitForAsync(() => File.Exists(artworkPath));

        Assert.True((await File.ReadAllBytesAsync(artworkPath)).AsSpan().StartsWith(
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }));
        store.TryCommit(
            source,
            PlaybackState.Playing,
            track,
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);
        await WaitForAsync(() => !File.Exists(artworkPath));
    }

    [Fact]
    public async Task HistoryUsesOrderedTrackIdentityTransitionsWithoutStateDuplicates()
    {
        using var directory = new TemporaryDirectory();
        var historyPath = Path.Combine(directory.Path, "history.txt");
        var store = CreateStore();
        await using var manager = new OutputManager(
            store,
            new ArtworkCache(),
            new OutputSettings
            {
                History = new HistoryOutputSettings
                {
                    Enabled = true,
                    FilePath = historyPath,
                    Template = "{nowPlaying}",
                },
            });
        manager.Start();
        var source = SourceDescriptor.WindowsMedia("Player.App");
        var first = TrackMetadata.Create("A", "Artist", null);
        store.TryCommit(
            source,
            PlaybackState.Playing,
            first,
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);
        store.TryCommit(
            source,
            PlaybackState.Paused,
            first,
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);
        store.TryCommit(
            source,
            PlaybackState.Playing,
            TrackMetadata.Create("B", "Artist", null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);

        await WaitForAsync(() => TryGetHistoryLineCount(historyPath) == 2);

        Assert.Equal(
            ["Artist - A", "Artist - B"],
            await File.ReadAllLinesAsync(historyPath));
    }

    [Fact]
    public async Task OneLockedTargetDoesNotPreventOtherOutputs()
    {
        using var directory = new TemporaryDirectory();
        var lockedPath = Path.Combine(directory.Path, "locked.txt");
        var jsonPath = Path.Combine(directory.Path, "state.json");
        await File.WriteAllTextAsync(lockedPath, "old");
        await using var locked = new FileStream(
            lockedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var store = CreateStore();
        await using var manager = new OutputManager(
            store,
            new ArtworkCache(),
            new OutputSettings
            {
                Text =
                [
                    new TextOutputSettings
                    {
                        Enabled = true,
                        Name = "Locked",
                        FilePath = lockedPath,
                    },
                ],
                Json = new JsonOutputSettings
                {
                    Enabled = true,
                    FilePath = jsonPath,
                },
            });
        manager.Start();
        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("Title", "Artist", null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);

        await WaitForAsync(() => File.Exists(jsonPath)
            && manager.GetStatus().FaultedCount == 1);

        Assert.Equal("old", await File.ReadAllTextAsync(lockedPath));
        Assert.Equal(3, JsonDocument.Parse(
            await File.ReadAllTextAsync(jsonPath)).RootElement
            .GetProperty("protocolVersion").GetInt32());
    }

    [Fact]
    public async Task SettingsUpdateImmediatelyRebuildsCurrentOutputsWithoutHistoryBackfill()
    {
        using var directory = new TemporaryDirectory();
        var textPath = Path.Combine(directory.Path, "now-playing.txt");
        var historyPath = Path.Combine(directory.Path, "history.txt");
        var store = CreateStore();
        await using var manager = new OutputManager(
            store,
            new ArtworkCache(),
            new OutputSettings());
        manager.Start();
        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("A", "Artist", null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);
        manager.UpdateSettings(new OutputSettings
        {
            Text =
            [
                new TextOutputSettings
                {
                    Enabled = true,
                    Name = "Current",
                    FilePath = textPath,
                },
            ],
            History = new HistoryOutputSettings
            {
                Enabled = true,
                FilePath = historyPath,
                Template = "{nowPlaying}",
            },
        });

        await WaitForAsync(() => File.Exists(textPath));

        Assert.Equal("Artist - A", await File.ReadAllTextAsync(textPath));
        Assert.False(File.Exists(historyPath));
        store.TryCommit(
            SourceDescriptor.WindowsMedia("Player.App"),
            PlaybackState.Playing,
            TrackMetadata.Create("B", "Artist", null),
            artwork: null,
            DateTimeOffset.UtcNow,
            out _);
        await WaitForAsync(() => TryGetHistoryLineCount(historyPath) == 1);
        Assert.Equal(["Artist - B"], await File.ReadAllLinesAsync(historyPath));
    }

    private static NowPlayingStore CreateStore()
    {
        return new NowPlayingStore(
            NowPlayingSnapshot.CreateInitial(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    private static TextOutputSettings CreateTextOutput(
        string name,
        string path,
        NoMediaOutputBehavior behavior,
        string placeholder = "")
    {
        return new TextOutputSettings
        {
            Enabled = true,
            Name = name,
            FilePath = path,
            NoMediaBehavior = behavior,
            NoMediaTemplate = placeholder,
        };
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private static int TryGetHistoryLineCount(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
