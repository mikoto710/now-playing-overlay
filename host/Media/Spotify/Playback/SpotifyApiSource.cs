using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.Spotify.Playback;

internal sealed class SpotifyApiSource : IMediaSourceProvider
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan StaleGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan[] TransientBackoff =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(40),
        TimeSpan.FromSeconds(60),
    ];

    private readonly object _gate = new();
    private readonly Func<SpotifyClientId, CancellationToken, Task<SpotifyPlaybackResult>> _readPlayback;
    private readonly IAsyncDisposable? _ownedClient;
    private readonly HttpClient _artworkHttpClient;
    private readonly HttpClient? _ownedArtworkHttpClient;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> _delay;
    private readonly ILogger<SpotifyApiSource> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<Task> _pollTasks = [];
    private SpotifyClientId? _clientId;
    private SourceDescriptor? _selection;
    private SourceManagerState _state = SourceManagerState.Unconfigured;
    private SessionObservation _observation =
        SessionObservation.Create(null, PlaybackState.Unavailable);
    private PublishedValue _published = PublishedValue.Unavailable;
    private DateTimeOffset? _lastSuccessAt;
    private CancellationTokenSource? _pollCancellation;
    private long _configurationGeneration;
    private bool _disposeStarted;
    private bool _disposed;

    public SpotifyApiSource(
        SpotifyCurrentlyPlayingClient client,
        SpotifyClientId? initialClientId = null,
        HttpClient? artworkHttpClient = null,
        ILogger<SpotifyApiSource>? logger = null)
        : this(
            GetReadPlayback(client),
            initialClientId,
            artworkHttpClient ?? new HttpClient(),
            artworkHttpClient is null,
            client,
            TimeProvider.System,
            DefaultDelayAsync,
            logger ?? NullLogger<SpotifyApiSource>.Instance)
    {
    }

    internal SpotifyApiSource(
        Func<SpotifyClientId, CancellationToken, Task<SpotifyPlaybackResult>> readPlayback,
        SpotifyClientId? initialClientId,
        HttpClient artworkHttpClient,
        TimeProvider timeProvider,
        Func<TimeSpan, CancellationToken, ValueTask> delay,
        ILogger<SpotifyApiSource>? logger = null)
        : this(
            readPlayback,
            initialClientId,
            artworkHttpClient,
            ownsArtworkHttpClient: false,
            ownedClient: null,
            timeProvider,
            delay,
            logger ?? NullLogger<SpotifyApiSource>.Instance)
    {
    }

    private SpotifyApiSource(
        Func<SpotifyClientId, CancellationToken, Task<SpotifyPlaybackResult>> readPlayback,
        SpotifyClientId? initialClientId,
        HttpClient artworkHttpClient,
        bool ownsArtworkHttpClient,
        IAsyncDisposable? ownedClient,
        TimeProvider timeProvider,
        Func<TimeSpan, CancellationToken, ValueTask> delay,
        ILogger<SpotifyApiSource> logger)
    {
        _readPlayback = readPlayback ?? throw new ArgumentNullException(nameof(readPlayback));
        _clientId = initialClientId;
        _artworkHttpClient = artworkHttpClient ?? throw new ArgumentNullException(nameof(artworkHttpClient));
        _ownedArtworkHttpClient = ownsArtworkHttpClient ? artworkHttpClient : null;
        _ownedClient = ownedClient;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler? Changed;

    public SourceProvider Provider => SourceProvider.SpotifyApi;

    public SourceManagerState GetState()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _state;
        }
    }

    public void SetClientId(SpotifyClientId? clientId)
    {
        var notify = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_clientId == clientId)
            {
                if (_selection is null
                    || _state.Reason is not (SourceStatusReason.AuthorizationRequired
                        or SourceStatusReason.Forbidden))
                {
                    return;
                }
            }

            _clientId = clientId;
            RestartPollingLocked();
            notify = _selection is not null;
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSelection(SourceDescriptor? selection)
    {
        ValidateSelection(selection);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Equals(_selection?.Key, selection?.Key))
            {
                return;
            }

            _selection = selection;
            RestartPollingLocked();
        }
    }

    public ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ValueTask.FromResult(_observation);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] polling;
        lock (_gate)
        {
            if (_disposeStarted)
            {
                return;
            }

            _disposeStarted = true;
            _disposed = true;
            _shutdown.Cancel();
            _pollCancellation?.Cancel();
            polling = _pollTasks.ToArray();
        }

        await Task.WhenAll(polling.Select(IgnoreCancellationAsync));
        if (_ownedClient is not null)
        {
            await _ownedClient.DisposeAsync();
        }

        _ownedArtworkHttpClient?.Dispose();
        _pollCancellation?.Dispose();
        _shutdown.Dispose();
    }

    private void RestartPollingLocked()
    {
        _configurationGeneration = checked(_configurationGeneration + 1);
        _pollCancellation?.Cancel();
        _pollCancellation?.Dispose();
        _pollCancellation = null;
        _lastSuccessAt = null;
        _published = PublishedValue.Unavailable;
        _observation = SessionObservation.Create(
            _selection,
            PlaybackState.Unavailable);

        if (_selection is null)
        {
            _state = SourceManagerState.Unconfigured;
            return;
        }

        if (_clientId is null)
        {
            _state = new SourceManagerState(
                _selection,
                SourceStatus.Unavailable,
                SourceStatusReason.AuthorizationRequired);
            return;
        }

        _state = new SourceManagerState(
            _selection,
            SourceStatus.Starting,
            SourceStatusReason.Starting);
        _pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var task = PollAsync(
            _configurationGeneration,
            _clientId.Value,
            _pollCancellation.Token);
        _pollTasks.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                lock (_gate)
                {
                    _pollTasks.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task PollAsync(
        long generation,
        SpotifyClientId clientId,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        var transientFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan nextDelay;
            try
            {
                var result = await _readPlayback(clientId, cancellationToken);
                transientFailures = 0;
                PublishSuccess(generation, result);
                nextDelay = PollInterval;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (SpotifyReauthorizationRequiredException error)
            {
                PublishUnavailable(generation, SourceStatusReason.AuthorizationRequired);
                _logger.LogWarning(error, "Spotify authorization must be renewed.");
                return;
            }
            catch (SpotifyTokenRequestException error)
            {
                if ((int)error.StatusCode == 429)
                {
                    PublishUnavailable(generation, SourceStatusReason.RateLimited);
                    nextDelay = error.RetryAfter ?? PollInterval;
                }
                else if ((int)error.StatusCode >= 500)
                {
                    transientFailures++;
                    PublishTransientFailure(generation, SourceStatusReason.ServiceUnavailable);
                    nextDelay = GetTransientBackoff(transientFailures);
                }
                else
                {
                    PublishUnavailable(generation, SourceStatusReason.AuthorizationRequired);
                    _logger.LogWarning(error, "Spotify token refresh failed and requires user action.");
                    return;
                }
            }
            catch (SpotifyApiRequestException error) when (
                error.FailureKind == SpotifyApiFailureKind.Unauthorized)
            {
                PublishUnavailable(generation, SourceStatusReason.AuthorizationRequired);
                _logger.LogWarning(error, "Spotify rejected the refreshed access token.");
                return;
            }
            catch (SpotifyApiRequestException error) when (
                error.FailureKind == SpotifyApiFailureKind.Forbidden)
            {
                PublishUnavailable(generation, SourceStatusReason.Forbidden);
                _logger.LogWarning(error, "Spotify denied currently-playing access.");
                return;
            }
            catch (SpotifyApiRequestException error) when (
                error.FailureKind == SpotifyApiFailureKind.RateLimited)
            {
                PublishUnavailable(generation, SourceStatusReason.RateLimited);
                nextDelay = error.RetryAfter ?? PollInterval;
            }
            catch (SpotifyApiRequestException error) when (
                error.FailureKind is SpotifyApiFailureKind.Transient
                    or SpotifyApiFailureKind.InvalidResponse)
            {
                transientFailures++;
                PublishTransientFailure(generation, SourceStatusReason.ServiceUnavailable);
                nextDelay = GetTransientBackoff(transientFailures);
            }
            catch (TaskCanceledException error)
            {
                transientFailures++;
                PublishTransientFailure(generation, SourceStatusReason.NetworkUnavailable);
                nextDelay = GetTransientBackoff(transientFailures);
                _logger.LogDebug(error, "Spotify currently-playing request timed out.");
            }
            catch (HttpRequestException error)
            {
                transientFailures++;
                PublishTransientFailure(generation, SourceStatusReason.NetworkUnavailable);
                nextDelay = GetTransientBackoff(transientFailures);
                _logger.LogDebug(error, "Spotify currently-playing request failed over the network.");
            }
            catch (Exception error)
            {
                PublishFaulted(generation, error);
                return;
            }

            try
            {
                await _delay(nextDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private void PublishSuccess(long generation, SpotifyPlaybackResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        SessionObservation observation;
        PublishedValue published;
        SourceStatusReason reason;
        SourceDescriptor selection;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation))
            {
                return;
            }

            selection = _selection!;
        }

        switch (result.Kind)
        {
            case SpotifyPlaybackResultKind.Track when result.Track is not null:
                var track = result.Track;
                var metadata = TrackMetadata.Create(
                    track.Title,
                    track.Artist,
                    track.AlbumTitle,
                    playbackType: MediaPlaybackKind.Music,
                    providerTrackId: track.TrackId);
                observation = SessionObservation.Create(
                    selection,
                    track.IsPlaying ? PlaybackState.Playing : PlaybackState.Paused,
                    metadata,
                    track.ArtworkUri is null
                        ? null
                        : new SpotifyArtworkReader(track.ArtworkUri, _artworkHttpClient));
                published = PublishedValue.FromTrack(track);
                reason = SourceStatusReason.None;
                break;
            case SpotifyPlaybackResultKind.Idle:
                observation = SessionObservation.Create(selection, PlaybackState.Idle);
                published = PublishedValue.Idle;
                reason = SourceStatusReason.None;
                break;
            case SpotifyPlaybackResultKind.Unsupported:
                observation = SessionObservation.Create(selection, PlaybackState.Idle);
                published = PublishedValue.Unsupported;
                reason = SourceStatusReason.Unsupported;
                break;
            default:
                throw new InvalidDataException("Spotify playback result is inconsistent.");
        }

        var changed = false;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation))
            {
                return;
            }

            _lastSuccessAt = _timeProvider.GetUtcNow();
            changed = !Equals(_published, published)
                || _state.Status != SourceStatus.Available
                || _state.Reason != reason;
            _observation = observation;
            _published = published;
            _state = new SourceManagerState(selection, SourceStatus.Available, reason);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PublishTransientFailure(long generation, SourceStatusReason reason)
    {
        var notify = false;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation))
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            if (_lastSuccessAt is { } lastSuccess && now - lastSuccess <= StaleGrace)
            {
                notify = _state.Status != SourceStatus.Available
                    || _state.Reason != SourceStatusReason.Stale;
                _state = new SourceManagerState(
                    _selection,
                    SourceStatus.Available,
                    SourceStatusReason.Stale);
            }
            else
            {
                notify = !Equals(_published, PublishedValue.Unavailable)
                    || _state.Status != SourceStatus.Unavailable
                    || _state.Reason != reason;
                _observation = SessionObservation.Create(_selection, PlaybackState.Unavailable);
                _published = PublishedValue.Unavailable;
                _state = new SourceManagerState(_selection, SourceStatus.Unavailable, reason);
            }
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PublishUnavailable(long generation, SourceStatusReason reason)
    {
        var notify = false;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation))
            {
                return;
            }

            notify = !Equals(_published, PublishedValue.Unavailable)
                || _state.Status != SourceStatus.Unavailable
                || _state.Reason != reason;
            _lastSuccessAt = null;
            _observation = SessionObservation.Create(_selection, PlaybackState.Unavailable);
            _published = PublishedValue.Unavailable;
            _state = new SourceManagerState(_selection, SourceStatus.Unavailable, reason);
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PublishFaulted(long generation, Exception error)
    {
        var notify = false;
        lock (_gate)
        {
            if (!IsCurrentLocked(generation))
            {
                return;
            }

            notify = _state.Status != SourceStatus.Faulted;
            _observation = SessionObservation.Create(_selection, PlaybackState.Unavailable);
            _published = PublishedValue.Unavailable;
            _state = new SourceManagerState(
                _selection,
                SourceStatus.Faulted,
                SourceStatusReason.Faulted);
        }

        _logger.LogError(error, "Spotify media source faulted.");
        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsCurrentLocked(long generation)
    {
        return !_disposed
            && _selection is not null
            && _configurationGeneration == generation;
    }

    private static TimeSpan GetTransientBackoff(int failureCount)
    {
        var index = Math.Clamp(failureCount - 1, 0, TransientBackoff.Length - 1);
        return TransientBackoff[index];
    }

    private static void ValidateSelection(SourceDescriptor? selection)
    {
        if (selection is not null && selection.Key != SourceKey.SpotifyApi())
        {
            throw new ArgumentException(
                "Spotify API source only accepts the current-account Spotify descriptor.",
                nameof(selection));
        }
    }

    private static Func<SpotifyClientId, CancellationToken, Task<SpotifyPlaybackResult>> GetReadPlayback(
        SpotifyCurrentlyPlayingClient? client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.GetCurrentlyPlayingAsync;
    }

    private static ValueTask DefaultDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return new ValueTask(Task.Delay(delay, cancellationToken));
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record PublishedValue(
        SpotifyPlaybackResultKind? Kind,
        string? TrackId,
        string? Title,
        string? Artist,
        string? AlbumTitle,
        string? ArtworkUri,
        bool IsPlaying)
    {
        public static PublishedValue Unavailable { get; } = new(null, null, null, null, null, null, false);

        public static PublishedValue Idle { get; } = new(
            SpotifyPlaybackResultKind.Idle,
            null,
            null,
            null,
            null,
            null,
            false);

        public static PublishedValue Unsupported { get; } = new(
            SpotifyPlaybackResultKind.Unsupported,
            null,
            null,
            null,
            null,
            null,
            false);

        public static PublishedValue FromTrack(SpotifyPlaybackTrack track)
        {
            return new PublishedValue(
                SpotifyPlaybackResultKind.Track,
                track.TrackId,
                track.Title,
                track.Artist,
                track.AlbumTitle,
                track.ArtworkUri?.AbsoluteUri,
                track.IsPlaying);
        }
    }
}
