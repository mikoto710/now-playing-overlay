using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.Media.Control;

namespace NowPlayingOverlay.SessionProbe;

internal sealed class MediaSessionProbe : IAsyncDisposable
{
    private readonly ProbeLogSink _sink;
    private readonly ThumbnailInspector _thumbnailInspector;
    private readonly string? _exerciseSource;
    private readonly ConcurrentDictionary<GlobalSystemMediaTransportControlsSession, SessionSubscription>
        _subscriptions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionReads =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _knownSourceCounts = new(StringComparer.Ordinal);

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private readonly SemaphoreSlim _sessionsRefresh = new(1, 1);
    private long _readSequence;

    public MediaSessionProbe(ProbeLogSink sink, string? exerciseSource)
    {
        _sink = sink;
        _thumbnailInspector = new ThumbnailInspector(sink);
        _exerciseSource = exerciseSource;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _sink.WriteAsync(
            "probe-started",
            details: new
            {
                Environment.OSVersion,
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            });
        await _sink.WriteAsync("session-manager-request-started");
        var stopwatch = Stopwatch.StartNew();
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        await _sink.WriteAsync(
            "session-manager-request-completed",
            details: new { elapsedMilliseconds = stopwatch.ElapsedMilliseconds });

        _manager.SessionsChanged += OnSessionsChanged;
        await RefreshSessionsAsync("initial-enumeration");

        if (_exerciseSource is not null)
        {
            var exercise = new MediaSessionControlExercise(_sink);
            await exercise.RunAsync(_manager, _exerciseSource, cancellationToken);
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        finally
        {
            await _sink.WriteAsync("probe-stopping");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
        }

        foreach (var subscription in _subscriptions.Values)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        foreach (var semaphore in _sessionReads.Values)
        {
            semaphore.Dispose();
        }

        _sessionReads.Clear();
        _sessionsRefresh.Dispose();
        await _sink.WriteAsync("probe-stopped");
    }

    private async void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        try
        {
            await _sink.WriteAsync("sessions-changed");
            await RefreshSessionsAsync("sessions-changed");
        }
        catch (Exception exception)
        {
            await WriteErrorAsync("sessions-changed-failed", null, exception);
        }
    }

    private async Task RefreshSessionsAsync(string reason)
    {
        await _sessionsRefresh.WaitAsync();
        try
        {
            var manager = _manager ?? throw new InvalidOperationException("Session manager is not initialized.");
            var sessions = manager.GetSessions().ToArray();
            await _sink.WriteAsync(
                "sessions-enumerated",
                details: new
                {
                    reason,
                    count = sessions.Length,
                    sourceAppUserModelIds = sessions.Select(session => session.SourceAppUserModelId).ToArray(),
                });

            var sessionsBySource = sessions
                .GroupBy(session => session.SourceAppUserModelId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var duplicate in sessionsBySource.Where(pair => pair.Value.Length > 1))
            {
                await _sink.WriteAsync(
                    "duplicate-source-app-user-model-id",
                    duplicate.Key,
                    new { count = duplicate.Value.Length });
            }

            foreach (var source in _knownSourceCounts.Keys.Union(sessionsBySource.Keys, StringComparer.Ordinal))
            {
                var previousCount = _knownSourceCounts.GetValueOrDefault(source);
                var currentCount = sessionsBySource.GetValueOrDefault(source)?.Length ?? 0;
                if (currentCount > previousCount)
                {
                    await _sink.WriteAsync(
                        "session-added",
                        source,
                        new { previousCount, currentCount });
                }
                else if (currentCount < previousCount)
                {
                    await _sink.WriteAsync(
                        "session-removed",
                        source,
                        new { previousCount, currentCount });
                }
            }

            foreach (var subscription in _subscriptions.Values)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
            _knownSourceCounts.Clear();
            foreach (var session in sessions)
            {
                var source = session.SourceAppUserModelId;
                _knownSourceCounts[source] = sessionsBySource[source].Length;
                var subscription = new SessionSubscription(session, OnSessionSignal);
                if (_subscriptions.TryAdd(session, subscription))
                {
                    _sessionReads.GetOrAdd(source, static _ => new SemaphoreSlim(1, 1));
                }
                else
                {
                    subscription.Dispose();
                }

                await ReadSessionAsync(session, reason);
            }
        }
        finally
        {
            _sessionsRefresh.Release();
        }
    }

    private async void OnSessionSignal(
        GlobalSystemMediaTransportControlsSession session,
        string signal)
    {
        try
        {
            await _sink.WriteAsync(signal, session.SourceAppUserModelId);
            await ReadSessionAsync(session, signal);
        }
        catch (Exception exception)
        {
            await WriteErrorAsync("session-event-read-failed", session.SourceAppUserModelId, exception);
        }
    }

    private async Task ReadSessionAsync(
        GlobalSystemMediaTransportControlsSession session,
        string reason)
    {
        if (!_sessionReads.TryGetValue(session.SourceAppUserModelId, out var readLock))
        {
            return;
        }

        await readLock.WaitAsync();
        try
        {
            var readId = Interlocked.Increment(ref _readSequence);
            var source = session.SourceAppUserModelId;
            var stopwatch = Stopwatch.StartNew();
            await _sink.WriteAsync("session-read-started", source, new { readId, reason });

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            await _sink.WriteAsync(
                "playback-read",
                source,
                new
                {
                    readId,
                    status = playback.PlaybackStatus.ToString(),
                    playbackType = playback.PlaybackType?.ToString(),
                    autoRepeatMode = playback.AutoRepeatMode?.ToString(),
                    playbackRate = playback.PlaybackRate,
                    shuffleActive = playback.IsShuffleActive,
                    controls = new
                    {
                        playback.Controls.IsPlayEnabled,
                        playback.Controls.IsPauseEnabled,
                        playback.Controls.IsStopEnabled,
                        playback.Controls.IsNextEnabled,
                        playback.Controls.IsPreviousEnabled,
                    },
                    timeline = new
                    {
                        positionMilliseconds = timeline.Position.TotalMilliseconds,
                        startMilliseconds = timeline.StartTime.TotalMilliseconds,
                        endMilliseconds = timeline.EndTime.TotalMilliseconds,
                        minSeekMilliseconds = timeline.MinSeekTime.TotalMilliseconds,
                        maxSeekMilliseconds = timeline.MaxSeekTime.TotalMilliseconds,
                        timeline.LastUpdatedTime,
                    },
                });

            await _sink.WriteAsync("media-properties-read-started", source, new { readId });
            var mediaStopwatch = Stopwatch.StartNew();
            var media = await session.TryGetMediaPropertiesAsync();
            await _sink.WriteAsync(
                "media-properties-read-completed",
                source,
                new
                {
                    readId,
                    elapsedMilliseconds = mediaStopwatch.ElapsedMilliseconds,
                    media.Title,
                    media.Artist,
                    media.AlbumTitle,
                    media.AlbumArtist,
                    media.Subtitle,
                    media.TrackNumber,
                    playbackType = media.PlaybackType?.ToString(),
                    genres = media.Genres.ToArray(),
                    hasThumbnail = media.Thumbnail is not null,
                });

            if (media.Thumbnail is not null)
            {
                await _thumbnailInspector.InspectAsync(source, readId, media.Thumbnail);
            }
            else
            {
                await _sink.WriteAsync("thumbnail-missing", source, new { readId });
            }

            await _sink.WriteAsync(
                "session-read-completed",
                source,
                new { readId, elapsedMilliseconds = stopwatch.ElapsedMilliseconds });
        }
        catch (Exception exception)
        {
            await WriteErrorAsync("session-read-failed", session.SourceAppUserModelId, exception);
        }
        finally
        {
            readLock.Release();
        }
    }

    private Task WriteErrorAsync(
        string eventName,
        string? source,
        Exception exception,
        object? context = null)
    {
        return _sink.WriteAsync(
            eventName,
            source,
            new
            {
                context,
                exceptionType = exception.GetType().FullName,
                exception.HResult,
                exception.Message,
            });
    }

    private sealed class SessionSubscription : IDisposable
    {
        private readonly GlobalSystemMediaTransportControlsSession _session;
        private readonly Action<GlobalSystemMediaTransportControlsSession, string> _signal;

        public SessionSubscription(
            GlobalSystemMediaTransportControlsSession session,
            Action<GlobalSystemMediaTransportControlsSession, string> signal)
        {
            _session = session;
            _signal = signal;
            SourceAppUserModelId = session.SourceAppUserModelId;
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }

        public string SourceAppUserModelId { get; }

        public void Dispose()
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        private void OnMediaPropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            MediaPropertiesChangedEventArgs args)
        {
            _signal(sender, "media-properties-changed");
        }

        private void OnPlaybackInfoChanged(
            GlobalSystemMediaTransportControlsSession sender,
            PlaybackInfoChangedEventArgs args)
        {
            _signal(sender, "playback-info-changed");
        }

        private void OnTimelinePropertiesChanged(
            GlobalSystemMediaTransportControlsSession sender,
            TimelinePropertiesChangedEventArgs args)
        {
            _signal(sender, "timeline-properties-changed");
        }
    }
}
