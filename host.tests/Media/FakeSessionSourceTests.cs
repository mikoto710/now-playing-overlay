using NowPlayingOverlay.Host.Media;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Tests.Media;

public sealed class FakeSessionSourceTests
{
    [Fact]
    public async Task PublishMakesScriptedStateReadableAndSignalsChange()
    {
        await using var source = new FakeSessionSource();
        var changed = 0;
        source.Changed += (_, _) => changed++;
        var observation = SessionObservation.Create(
            "Spotify.exe",
            PlaybackState.Playing,
            TrackMetadata.Create("Track", "Artist", "Album"));

        source.Publish(observation);
        var result = await source.ReadAsync(CancellationToken.None);

        Assert.Same(observation, result);
        Assert.Equal(1, changed);
        Assert.True(source.IsAvailable);
    }

    [Fact]
    public async Task ScriptSupportsOrderedPlaybackAndPauseTransitions()
    {
        await using var source = new FakeSessionSource();
        var track = TrackMetadata.Create("Track", "Artist", null);
        var seen = new List<PlaybackState>();
        source.Changed += (_, _) => seen.Add(
            source.ReadAsync(CancellationToken.None).Result.Playback);
        var steps = new[]
        {
            new FakeSessionStep(
                TimeSpan.Zero,
                SessionObservation.Create("Spotify.exe", PlaybackState.Playing, track)),
            new FakeSessionStep(
                TimeSpan.Zero,
                SessionObservation.Create("Spotify.exe", PlaybackState.Paused, track)),
        };

        await source.RunScriptAsync(steps);

        Assert.Equal([PlaybackState.Playing, PlaybackState.Paused], seen);
    }

    [Fact]
    public async Task DelayedArtworkHonorsCancellation()
    {
        var reader = new DelayedArtworkReader(
            ArtworkPayload.Create([1]),
            TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.ReadAsync(cancellation.Token));
    }
}
