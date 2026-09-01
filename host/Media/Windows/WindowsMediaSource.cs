using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.Windows;

/// <summary>
/// Owns the GSMTC catalog and binding to one selected media session.
/// </summary>
/// <remarks>
/// No selection -> Unconfigured; selection -> Starting; one exact usable session -> Available.
/// No exact session -> Unavailable/Missing; unresolved exact matches -> Unavailable/Ambiguous.
/// Expected GSMTC or COM loss -> Unavailable/PlatformUnavailable; unexpected background failure
/// -> Faulted. Configuration generation rejects work for an old selection; binding generation
/// retries reads when the chosen session changes. Catalog and bound-session events only request a
/// reread. This source owns and disposes the manager, session binding, and read cancellation.
/// </remarks>
internal sealed class WindowsMediaSource : IMediaSourceProvider
{
    // Protects selection, bindings, status, generations, and read cancellation.
    private readonly object _gate = new();
    // Serializes manager creation and disposal.
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private readonly IMediaSessionManagerFactory _managerFactory;
    private readonly WindowsMediaSessionMatcher _matcher;
    private readonly ILogger<WindowsMediaSource> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private IMediaSessionManager? _manager;
    private IMediaSessionAdapter? _boundSession;
    private SourceDescriptor? _selection;
    private SourceManagerState _state = SourceManagerState.Unconfigured;
    private Exception? _backgroundError;
    private CancellationTokenSource? _activeReadCancellation;
    private long _configurationGeneration; // Rejects reads for an old selection.
    private long _bindingGeneration; // Retries reads when the binding changes.
    private bool _disposeStarted;
    private bool _disposed;

    public WindowsMediaSource(
        IMediaSessionManagerFactory managerFactory,
        WindowsMediaSessionMatcher matcher,
        ILogger<WindowsMediaSource> logger)
    {
        _managerFactory = managerFactory ?? throw new ArgumentNullException(nameof(managerFactory));
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler? Changed;

    public SourceProvider Provider => SourceProvider.WindowsMedia;

    public SourceManagerState GetState()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _state;
        }
    }

    public void SetSelection(SourceDescriptor? selection)
    {
        ValidateSelection(selection);
        IMediaSessionAdapter? previous;
        IMediaSessionManager? managerToDispose = null;
        IMediaSessionManager? managerToRefresh = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Equals(_selection?.Key, selection?.Key))
            {
                return;
            }

            previous = _boundSession;
            if (previous is not null)
            {
                previous.Changed -= OnBoundSessionChanged;
            }

            _boundSession = null;
            _selection = selection;
            _state = selection is null
                ? SourceManagerState.Unconfigured
                : new SourceManagerState(
                    selection,
                    SourceStatus.Starting,
                    SourceStatusReason.Starting);
            _backgroundError = null;
            _configurationGeneration = checked(_configurationGeneration + 1);
            _bindingGeneration = checked(_bindingGeneration + 1);
            _activeReadCancellation?.Cancel();

            if (selection is null)
            {
                managerToDispose = _manager;
                _manager = null;
                if (managerToDispose is not null)
                {
                    managerToDispose.SessionsChanged -= OnSessionsChanged;
                }
            }
            else
            {
                managerToRefresh = _manager;
            }
        }

        previous?.Dispose();
        managerToDispose?.Dispose();
        if (managerToRefresh is not null)
        {
            TryRefreshBinding(managerToRefresh, notify: false);
        }
    }

    public async Task<SourceDiscoveryResult> RefreshSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SourceDescriptor? selection;
        lock (_gate)
        {
            selection = _selection;
        }

        if (selection is null)
        {
            var sources = await DiscoverInactiveSourcesAsync(cancellationToken);
            return new SourceDiscoveryResult(sources, GetState());
        }

        var initialized = await EnsureInitializedAsync(cancellationToken);
        IReadOnlyList<SourceDescriptor> activeSources = [];
        if (initialized)
        {
            IMediaSessionManager? manager;
            lock (_gate)
            {
                manager = _manager;
            }

            if (manager is not null)
            {
                try
                {
                    activeSources = RefreshBinding(manager);
                }
                catch (Exception error) when (IsExpectedPlatformFailure(error))
                {
                    SetPlatformUnavailable(error);
                }
                catch (Exception error)
                {
                    SetFaulted(error);
                    throw;
                }
                finally
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        return new SourceDiscoveryResult(activeSources, GetState());
    }

    public async ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            SourceDescriptor? selected;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                selected = _selection;
            }

            if (selected is null)
            {
                return SessionObservation.Create(null, PlaybackState.Unavailable);
            }

            if (!await EnsureInitializedAsync(cancellationToken))
            {
                return SessionObservation.Create(selected, PlaybackState.Unavailable);
            }

            IMediaSessionAdapter? session;
            Exception? backgroundError;
            long configurationGeneration;
            long bindingGeneration;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                selected = _selection;
                session = _boundSession;
                backgroundError = _backgroundError;
                configurationGeneration = _configurationGeneration;
                bindingGeneration = _bindingGeneration;
            }

            if (backgroundError is not null)
            {
                throw new InvalidOperationException("Windows media source monitoring failed.", backgroundError);
            }

            if (selected is null)
            {
                continue;
            }

            if (session is null)
            {
                return SessionObservation.Create(selected, PlaybackState.Unavailable);
            }

            using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _shutdown.Token);
            lock (_gate)
            {
                if (_configurationGeneration != configurationGeneration)
                {
                    return CreateCurrentUnavailableObservation();
                }

                if (_bindingGeneration != bindingGeneration
                    || !ReferenceEquals(_boundSession, session))
                {
                    continue;
                }

                _activeReadCancellation = readCancellation;
            }

            SessionObservation observation;
            try
            {
                observation = await session.ReadAsync(readCancellation.Token);
            }
            catch (OperationCanceledException) when (readCancellation.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested)
                {
                    throw;
                }

                if (HasConfigurationChanged(configurationGeneration))
                {
                    return CreateCurrentUnavailableObservation();
                }

                continue;
            }
            catch (Exception) when (HasConfigurationChanged(configurationGeneration))
            {
                return CreateCurrentUnavailableObservation();
            }
            catch (Exception) when (HasBindingChanged(
                session,
                configurationGeneration,
                bindingGeneration))
            {
                continue;
            }
            catch (Exception error) when (IsExpectedPlatformFailure(error))
            {
                MarkSessionUnavailable(
                    session,
                    configurationGeneration,
                    bindingGeneration,
                    error);
                return SessionObservation.Create(selected, PlaybackState.Unavailable);
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_activeReadCancellation, readCancellation))
                    {
                        _activeReadCancellation = null;
                    }
                }
            }

            if (HasConfigurationChanged(configurationGeneration))
            {
                return CreateCurrentUnavailableObservation();
            }

            if (HasBindingChanged(session, configurationGeneration, bindingGeneration))
            {
                continue;
            }

            SetAvailable(selected);
            return SessionObservation.Create(
                selected,
                observation.Playback,
                observation.Track,
                observation.ArtworkReader);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeStarted)
            {
                return;
            }

            _disposeStarted = true;
            _disposed = true;
        }

        Exception? firstError = CaptureFailure(_shutdown.Cancel, firstError: null);
        var initializationEntered = false;
        try
        {
            await _initialization.WaitAsync();
            initializationEntered = true;
            IMediaSessionManager? manager;
            IMediaSessionAdapter? boundSession;
            CancellationTokenSource? activeReadCancellation;
            lock (_gate)
            {
                manager = _manager;
                boundSession = _boundSession;
                activeReadCancellation = _activeReadCancellation;
                _manager = null;
                _boundSession = null;
                _configurationGeneration++;
                _bindingGeneration++;
            }

            firstError = Cancel(activeReadCancellation, firstError);

            if (manager is not null)
            {
                firstError = CaptureFailure(
                    () => manager.SessionsChanged -= OnSessionsChanged,
                    firstError);
            }

            if (boundSession is not null)
            {
                firstError = CaptureFailure(
                    () => boundSession.Changed -= OnBoundSessionChanged,
                    firstError);
                firstError = CaptureFailure(boundSession.Dispose, firstError);
            }

            if (manager is not null)
            {
                firstError = CaptureFailure(manager.Dispose, firstError);
            }
        }
        catch (Exception error)
        {
            firstError ??= error;
        }
        finally
        {
            if (initializationEntered)
            {
                firstError = CaptureFailure(
                    () => { _initialization.Release(); },
                    firstError);
            }
        }

        firstError = CaptureFailure(_shutdown.Dispose, firstError);
        firstError = CaptureFailure(_initialization.Dispose, firstError);

        if (firstError is not null)
        {
            ExceptionDispatchInfo.Capture(firstError).Throw();
        }
    }

    private async Task<IReadOnlyList<SourceDescriptor>> DiscoverInactiveSourcesAsync(
        CancellationToken cancellationToken)
    {
        await _initialization.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            IMediaSessionManager manager;
            try
            {
                manager = await _managerFactory.CreateAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error) when (IsExpectedPlatformFailure(error))
            {
                SetPlatformUnavailable(error);
                return [];
            }
            catch (Exception error)
            {
                SetFaulted(error);
                throw;
            }

            using (manager)
            {
                IReadOnlyList<IMediaSessionAdapter> sessions;
                try
                {
                    sessions = manager.GetSessions();
                }
                catch (Exception error) when (IsExpectedPlatformFailure(error))
                {
                    SetPlatformUnavailable(error);
                    return [];
                }
                catch (Exception error)
                {
                    SetFaulted(error);
                    throw;
                }

                try
                {
                    return BuildDescriptors(sessions);
                }
                finally
                {
                    DisposeSessions(sessions, except: null, previous: null);
                }
            }
        }
        finally
        {
            _initialization.Release();
        }
    }

    private async Task<bool> EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_manager is not null)
            {
                return true;
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
                    return true;
                }

                if (_selection is null)
                {
                    return false;
                }

                _state = new SourceManagerState(
                    _selection,
                    SourceStatus.Starting,
                    SourceStatusReason.Starting);
            }

            IMediaSessionManager manager;
            try
            {
                manager = await _managerFactory.CreateAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error) when (IsExpectedPlatformFailure(error))
            {
                SetPlatformUnavailable(error);
                return false;
            }
            catch (Exception error)
            {
                SetFaulted(error);
                throw;
            }

            manager.SessionsChanged += OnSessionsChanged;
            lock (_gate)
            {
                if (_disposed || _selection is null)
                {
                    manager.SessionsChanged -= OnSessionsChanged;
                    manager.Dispose();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return false;
                }

                _manager = manager;
            }

            try
            {
                RefreshBinding(manager);
                return true;
            }
            catch (Exception error) when (IsExpectedPlatformFailure(error))
            {
                SetPlatformUnavailable(error);
                return false;
            }
            catch (Exception error)
            {
                SetFaulted(error);
                throw;
            }
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

        if (manager is not null)
        {
            TryRefreshBinding(manager, notify: true);
        }
    }

    private void OnBoundSessionChanged(object? sender, EventArgs args)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void TryRefreshBinding(IMediaSessionManager manager, bool notify)
    {
        try
        {
            RefreshBinding(manager);
        }
        catch (Exception error) when (IsExpectedPlatformFailure(error))
        {
            SetPlatformUnavailable(error);
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _backgroundError = error;
                _state = new SourceManagerState(
                    _selection,
                    SourceStatus.Faulted,
                    SourceStatusReason.Faulted);
            }

            _logger.LogError(
                "Failed to refresh Windows media sessions. {Diagnostic}",
                SanitizedExceptionDiagnostics.Create(error));
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private IReadOnlyList<SourceDescriptor> RefreshBinding(IMediaSessionManager manager)
    {
        var sessions = manager.GetSessions();
        var descriptors = BuildDescriptors(sessions);
        var candidates = new List<WindowsMediaSessionCandidate>(sessions.Count);
        foreach (var session in sessions)
        {
            MediaSessionPlaybackStatus? playbackStatus;
            try
            {
                playbackStatus = session.GetPlaybackStatus();
            }
            catch (Exception error) when (IsExpectedPlatformFailure(error))
            {
                playbackStatus = null;
                _logger.LogWarning(
                    "Could not read playback status for media source {SourceAppUserModelId}. Error type {ErrorType}, HRESULT {ErrorHResult}.",
                    session.SourceAppUserModelId,
                    error.GetType().Name,
                    error.HResult);
            }

            candidates.Add(new WindowsMediaSessionCandidate(
                session,
                session.SourceAppUserModelId,
                playbackStatus));
        }

        SourceDescriptor? selected;
        long configurationGeneration;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(_manager, manager))
            {
                DisposeSessions(sessions, except: null, previous: null);
                return descriptors;
            }

            selected = _selection;
            configurationGeneration = _configurationGeneration;
        }

        var selection = selected is null
            ? WindowsMediaSessionSelection.Missing
            : _matcher.Select(selected.Key.InstanceId, candidates);
        var next = selection.Session;
        IMediaSessionAdapter? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_configurationGeneration != configurationGeneration
                || !ReferenceEquals(_manager, manager))
            {
                DisposeSessions(sessions, except: null, previous: null);
                return descriptors;
            }

            previous = _boundSession;
            if (previous is not null && !ReferenceEquals(previous, next))
            {
                previous.Changed -= OnBoundSessionChanged;
            }

            _boundSession = next;
            _backgroundError = null;
            _bindingGeneration = checked(_bindingGeneration + 1);
            if (next is not null && !ReferenceEquals(previous, next))
            {
                next.Changed += OnBoundSessionChanged;
            }

            _state = BuildState(selected, selection.Status);
        }

        if (previous is not null && !ReferenceEquals(previous, next))
        {
            previous.Dispose();
        }

        DisposeSessions(sessions, next, previous);
        LogSelection(selected, selection, descriptors.Count);
        return descriptors;
    }

    private void MarkSessionUnavailable(
        IMediaSessionAdapter session,
        long configurationGeneration,
        long bindingGeneration,
        Exception error)
    {
        var dispose = false;
        lock (_gate)
        {
            if (_configurationGeneration != configurationGeneration
                || _bindingGeneration != bindingGeneration
                || !ReferenceEquals(_boundSession, session))
            {
                return;
            }

            session.Changed -= OnBoundSessionChanged;
            _boundSession = null;
            _backgroundError = null;
            _bindingGeneration++;
            _state = new SourceManagerState(
                _selection,
                SourceStatus.Unavailable,
                SourceStatusReason.PlatformUnavailable);
            dispose = true;
        }

        if (dispose)
        {
            session.Dispose();
        }

        _logger.LogWarning(
            "The selected Windows media session became unavailable. Error type {ErrorType}, HRESULT {ErrorHResult}.",
            error.GetType().Name,
            error.HResult);
    }

    private void SetAvailable(SourceDescriptor selected)
    {
        lock (_gate)
        {
            if (Equals(_selection?.Key, selected.Key) && _boundSession is not null)
            {
                _state = new SourceManagerState(
                    selected,
                    SourceStatus.Available,
                    SourceStatusReason.None);
            }
        }
    }

    private void SetPlatformUnavailable(Exception error)
    {
        IMediaSessionAdapter? boundSession;
        lock (_gate)
        {
            boundSession = _boundSession;
            if (boundSession is not null)
            {
                boundSession.Changed -= OnBoundSessionChanged;
            }

            _boundSession = null;
            _backgroundError = null;
            _bindingGeneration++;
            _activeReadCancellation?.Cancel();
            _state = _selection is null
                ? SourceManagerState.Unconfigured
                : new SourceManagerState(
                    _selection,
                    SourceStatus.Unavailable,
                    SourceStatusReason.PlatformUnavailable);
        }

        boundSession?.Dispose();
        _logger.LogWarning(
            "Windows media sessions are temporarily unavailable. Error type {ErrorType}, HRESULT {ErrorHResult}.",
            error.GetType().Name,
            error.HResult);
    }

    private void SetFaulted(Exception error)
    {
        lock (_gate)
        {
            _backgroundError = error;
            _state = new SourceManagerState(
                _selection,
                SourceStatus.Faulted,
                SourceStatusReason.Faulted);
        }

        _logger.LogError(
            "Windows media source faulted. {Diagnostic}",
            SanitizedExceptionDiagnostics.Create(error));
    }

    private bool HasBindingChanged(
        IMediaSessionAdapter session,
        long configurationGeneration,
        long bindingGeneration)
    {
        lock (_gate)
        {
            return _configurationGeneration != configurationGeneration
                || _bindingGeneration != bindingGeneration
                || !ReferenceEquals(_boundSession, session);
        }
    }

    private bool HasConfigurationChanged(long configurationGeneration)
    {
        lock (_gate)
        {
            return _configurationGeneration != configurationGeneration;
        }
    }

    private SessionObservation CreateCurrentUnavailableObservation()
    {
        lock (_gate)
        {
            return SessionObservation.Create(_selection, PlaybackState.Unavailable);
        }
    }

    private static IReadOnlyList<SourceDescriptor> BuildDescriptors(
        IEnumerable<IMediaSessionAdapter> sessions)
    {
        return sessions
            .Select(session => SourceDescriptor.WindowsMedia(session.SourceAppUserModelId))
            .DistinctBy(descriptor => descriptor.Key.InstanceId, StringComparer.Ordinal)
            .OrderBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SourceManagerState BuildState(
        SourceDescriptor? selected,
        WindowsMediaSessionSelectionStatus selectionStatus)
    {
        if (selected is null)
        {
            return SourceManagerState.Unconfigured;
        }

        return selectionStatus switch
        {
            WindowsMediaSessionSelectionStatus.Selected =>
                new SourceManagerState(selected, SourceStatus.Available, SourceStatusReason.None),
            WindowsMediaSessionSelectionStatus.Missing =>
                new SourceManagerState(selected, SourceStatus.Unavailable, SourceStatusReason.Missing),
            WindowsMediaSessionSelectionStatus.Ambiguous =>
                new SourceManagerState(selected, SourceStatus.Unavailable, SourceStatusReason.Ambiguous),
            _ => throw new ArgumentOutOfRangeException(
                nameof(selectionStatus),
                selectionStatus,
                "Windows media selection status is invalid."),
        };
    }

    private void LogSelection(
        SourceDescriptor? selected,
        WindowsMediaSessionSelection selection,
        int sourceCount)
    {
        if (selected is null)
        {
            _logger.LogInformation("Windows Media source is not configured.");
        }
        else if (selection.Status == WindowsMediaSessionSelectionStatus.Selected)
        {
            _logger.LogInformation(
                "Bound the selected Windows media source from {MatchCount} exact candidate(s).",
                selection.MatchCount);
        }
        else if (selection.Status == WindowsMediaSessionSelectionStatus.Ambiguous)
        {
            _logger.LogWarning(
                "The selected Windows media source is ambiguous across {MatchCount} exact candidates.",
                selection.MatchCount);
        }
        else
        {
            _logger.LogInformation(
                "The selected Windows media source is absent among {SourceCount} source(s).",
                sourceCount);
        }
    }

    private static bool IsExpectedPlatformFailure(Exception error)
    {
        return error is COMException
            or UnauthorizedAccessException
            or InvalidOperationException;
    }

    private static Exception? Cancel(
        CancellationTokenSource? cancellation,
        Exception? firstError)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        return firstError;
    }

    private static Exception? CaptureFailure(Action action, Exception? firstError)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            firstError ??= error;
        }

        return firstError;
    }

    private static void ValidateSelection(SourceDescriptor? selection)
    {
        if (selection is not null && selection.Key.Provider != SourceProvider.WindowsMedia)
        {
            throw new ArgumentException(
                "WindowsMediaSource accepts only Windows Media selections.",
                nameof(selection));
        }
    }

    private static void DisposeSessions(
        IEnumerable<IMediaSessionAdapter> sessions,
        IMediaSessionAdapter? except,
        IMediaSessionAdapter? previous)
    {
        foreach (var session in sessions)
        {
            if (!ReferenceEquals(session, except) && !ReferenceEquals(session, previous))
            {
                session.Dispose();
            }
        }
    }
}
