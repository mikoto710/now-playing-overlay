using System.Diagnostics;
using Windows.Media.Control;

namespace NowPlayingOverlay.SessionProbe;

internal sealed class MediaSessionControlExercise
{
    private readonly ProbeLogSink _sink;

    public MediaSessionControlExercise(ProbeLogSink sink)
    {
        _sink = sink;
    }

    public async Task RunAsync(
        GlobalSystemMediaTransportControlsSessionManager manager,
        string sourceAppUserModelId,
        CancellationToken cancellationToken)
    {
        var matches = manager
            .GetSessions()
            .Where(session => string.Equals(
                session.SourceAppUserModelId,
                sourceAppUserModelId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await _sink.WriteAsync(
            "control-exercise-source-matched",
            sourceAppUserModelId,
            new { exactMatchCount = matches.Length });
        // Never control a guessed candidate; ambiguity is a probe result.
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Control exercise requires exactly one exact match for '{sourceAppUserModelId}', found {matches.Length}.");
        }

        var session = matches[0];
        // Allow Windows to publish each requested transition.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        await RunActionAsync(session, "pause", () => session.TryPauseAsync());
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await RunActionAsync(session, "play-after-pause", () => session.TryPlayAsync());
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await RunActionAsync(session, "stop", () => session.TryStopAsync());
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await RunActionAsync(session, "play-after-stop", () => session.TryPlayAsync());
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        // Create the A-to-B-to-C race the production coordinator must handle.
        for (var index = 1; index <= 3; index++)
        {
            await RunActionAsync(session, $"rapid-skip-{index}", () => session.TrySkipNextAsync());
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
        }

        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        await _sink.WriteAsync("control-exercise-completed", sourceAppUserModelId);
    }

    private async Task RunActionAsync(
        GlobalSystemMediaTransportControlsSession session,
        string action,
        Func<Windows.Foundation.IAsyncOperation<bool>> operation)
    {
        await _sink.WriteAsync("control-action-started", session.SourceAppUserModelId, new { action });
        var stopwatch = Stopwatch.StartNew();
        var accepted = await operation();
        await _sink.WriteAsync(
            "control-action-completed",
            session.SourceAppUserModelId,
            new { action, accepted, elapsedMilliseconds = stopwatch.ElapsedMilliseconds });
    }
}
