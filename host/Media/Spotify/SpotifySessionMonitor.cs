using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Media.Windows;

namespace NowPlayingOverlay.Host.Media.Spotify;

internal sealed class SpotifySessionMonitor : ISessionSource, ISessionSourceStatus
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private readonly IMediaSessionManagerFactory _managerFactory;
    private readonly SpotifySessionMatcher _matcher;
    private readonly ILogger<SpotifySessionMonitor> _logger;
    private IMediaSessionManager? _manager;
    private IMediaSessionAdapter? _boundSession;
    private SpotifySessionSelectionStatus _selectionStatus = SpotifySessionSelectionStatus.NotFound;
    private Exception? _backgroundError;
    private long _bindingGeneration;
    private bool _disposed;

    public SpotifySessionMonitor(
        IMediaSessionManagerFactory managerFactory,
        SpotifySessionMatcher matcher,
        ILogger<SpotifySessionMonitor> logger)
    {
        _managerFactory = managerFactory ?? throw new ArgumentNullException(nameof(managerFactory));
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler? Changed;

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return _manager is not null && _backgroundError is null;
            }
        }
    }

    internal SpotifySessionSelectionStatus SelectionStatus
    {
        get
        {
            lock (_gate)
            {
                return _selectionStatus;
            }
        }
    }

    public async ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        while (true)
        {
            IMediaSessionAdapter? session;
            Exception? backgroundError;
            long generation;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                session = _boundSession;
                generation = _bindingGeneration;
                backgroundError = _backgroundError;
            }

            if (backgroundError is not null)
            {
                throw new InvalidOperationException("Media session monitoring failed.", backgroundError);
            }

            if (session is null)
            {
                return SessionObservation.Create(null, PlaybackState.Unavailable);
            }

            SessionObservation observation;
            try
            {
                observation = await session.ReadAsync(cancellationToken);
            }
            catch when (HasBindingChanged(session, generation))
            {
                continue;
            }

            // A session replacement can complete an old WinRT read after rebinding.
            if (HasBindingChanged(session, generation))
            {
                continue;
            }

            return observation;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _initialization.WaitAsync();
        try
        {
            IMediaSessionManager? manager;
            IMediaSessionAdapter? boundSession;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                manager = _manager;
                boundSession = _boundSession;
                _manager = null;
                _boundSession = null;
                _bindingGeneration++;
            }

            if (manager is not null)
            {
                manager.SessionsChanged -= OnSessionsChanged;
            }

            if (boundSession is not null)
            {
                boundSession.Changed -= OnBoundSessionChanged;
                boundSession.Dispose();
            }

            manager?.Dispose();
        }
        finally
        {
            _initialization.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_manager is not null)
            {
                return;
            }
        }

        await _initialization.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_manager is not null)
                {
                    return;
                }
            }

            var manager = await _managerFactory.CreateAsync(cancellationToken);
            manager.SessionsChanged += OnSessionsChanged;
            lock (_gate)
            {
                if (_disposed)
                {
                    manager.SessionsChanged -= OnSessionsChanged;
                    manager.Dispose();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                }

                _manager = manager;
            }

            RefreshBinding(manager);
        }
        finally
        {
            _initialization.Release();
        }
    }

    private void OnSessionsChanged(object? sender, EventArgs args)
    {
        IMediaSessionManager? manager;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            manager = _manager;
        }

        try
        {
            if (manager is not null)
            {
                RefreshBinding(manager);
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _backgroundError = error;
            }

            _logger.LogError(error, "Failed to refresh Windows media sessions.");
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnBoundSessionChanged(object? sender, EventArgs args)
    {
        // Platform events only signal the coordinator; they never commit state directly.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshBinding(IMediaSessionManager manager)
    {
        var sessions = manager.GetSessions();
        var candidates = new List<SpotifySessionCandidate>(sessions.Count);
        foreach (var session in sessions)
        {
            MediaSessionPlaybackStatus? playbackStatus;
            try
            {
                playbackStatus = session.GetPlaybackStatus();
            }
            catch (Exception error)
            {
                playbackStatus = null;
                _logger.LogWarning(
                    error,
                    "Could not read playback status for media source {SourceAppUserModelId}.",
                    session.SourceAppUserModelId);
            }

            candidates.Add(new SpotifySessionCandidate(
                session,
                session.SourceAppUserModelId,
                playbackStatus));
        }

        var selection = _matcher.Select(candidates);
        var selected = selection.Session;
        IMediaSessionAdapter? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _boundSession;
            if (previous is not null && !ReferenceEquals(previous, selected))
            {
                previous.Changed -= OnBoundSessionChanged;
            }

            _boundSession = selected;
            _selectionStatus = selection.Status;
            _backgroundError = null;
            _bindingGeneration = checked(_bindingGeneration + 1);
            if (selected is not null && !ReferenceEquals(previous, selected))
            {
                selected.Changed += OnBoundSessionChanged;
            }
        }

        if (previous is not null && !ReferenceEquals(previous, selected))
        {
            previous.Dispose();
        }

        foreach (var session in sessions)
        {
            if (!ReferenceEquals(session, selected) && !ReferenceEquals(session, previous))
            {
                session.Dispose();
            }
        }

        LogSelection(selection, candidates);
    }

    private void LogSelection(
        SpotifySessionSelection selection,
        IReadOnlyCollection<SpotifySessionCandidate> candidates)
    {
        if (selection.Status == SpotifySessionSelectionStatus.Selected)
        {
            _logger.LogInformation(
                "Bound Spotify media session from {MatchCount} exact candidate(s).",
                selection.MatchCount);
        }
        else if (selection.Status == SpotifySessionSelectionStatus.Ambiguous)
        {
            _logger.LogWarning(
                "Spotify media session selection is ambiguous across {MatchCount} exact candidates.",
                selection.MatchCount);
        }
        else
        {
            _logger.LogInformation(
                "No verified Spotify media session found among {SourceCount} media source(s).",
                candidates.Count);
            _logger.LogDebug(
                "Observed media session sources: {Sources}.",
                candidates.Select(candidate => candidate.SourceAppUserModelId).ToArray());
        }
    }

    private bool HasBindingChanged(IMediaSessionAdapter session, long generation)
    {
        lock (_gate)
        {
            return _bindingGeneration != generation || !ReferenceEquals(_boundSession, session);
        }
    }
}
