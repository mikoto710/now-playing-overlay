using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.WindowTitles;

/// <summary>
/// Polls one stable Win32 target and exposes its parsed title as a complete observation.
/// </summary>
internal sealed class WindowTitleSource : IMediaSourceProvider
{
    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private readonly object _gate = new(); // Protects settings, selection, status, and fingerprints.
    private readonly IWindowTitleCatalog _catalog;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger<WindowTitleSource> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private WindowTitleSettings _settings;
    private SourceDescriptor? _selection;
    private SourceManagerState _state = SourceManagerState.Unconfigured;
    private ObservationFingerprint? _lastFingerprint;
    private Task? _monitor;
    private bool _faultLogged;
    private bool _disposed;

    public WindowTitleSource(
        IWindowTitleCatalog catalog,
        WindowTitleSettings settings,
        TimeSpan? pollInterval = null,
        ILogger<WindowTitleSource>? logger = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        settings = settings ?? throw new ArgumentNullException(nameof(settings));
        settings.Validate();
        _settings = settings;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _logger = logger ?? NullLogger<WindowTitleSource>.Instance;
        if (_pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }
    }

    public event EventHandler? Changed;

    public SourceProvider Provider => SourceProvider.WindowTitle;

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
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Equals(_selection?.Key, selection?.Key))
            {
                return;
            }

            _selection = selection;
            _lastFingerprint = null;
            _state = selection is null
                ? SourceManagerState.Unconfigured
                : new SourceManagerState(selection, SourceStatus.Starting, SourceStatusReason.Starting);
            if (selection is not null && _monitor is null)
            {
                _monitor = MonitorAsync(_shutdown.Token);
            }
        }
    }

    public void UpdateSettings(WindowTitleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var notify = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_settings == settings)
            {
                return;
            }

            _settings = settings;
            _lastFingerprint = null;
            notify = _selection is not null;
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task<WindowTitleDiscoveryResult> RefreshSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<WindowTitleWindow> windows;
        try
        {
            windows = _catalog.GetWindows();
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            RecordFault(error, "discovery");
            SourceManagerState state;
            lock (_gate)
            {
                state = _selection is null
                    ? SourceManagerState.Unconfigured
                    : new SourceManagerState(
                        _selection,
                        SourceStatus.Faulted,
                        SourceStatusReason.Faulted);
                _state = state;
            }

            return Task.FromResult(new WindowTitleDiscoveryResult([], state));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var candidates = windows
            .GroupBy(window => window.Target.InstanceId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var count = group.Count();
                return new WindowTitleCandidate(
                    first.Target,
                    count == 1 ? first.Title : string.Empty,
                    count);
            })
            .OrderBy(candidate => candidate.Target.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.CurrentTitle, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(new WindowTitleDiscoveryResult(candidates, GetState()));
    }

    public ValueTask<SessionObservation> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = Probe();
        lock (_gate)
        {
            if (!_disposed && _selection is not null)
            {
                _lastFingerprint = result.Fingerprint;
            }
        }

        return ValueTask.FromResult(result.Observation);
    }

    public async ValueTask DisposeAsync()
    {
        Task? monitor;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdown.Cancel();
            monitor = _monitor;
        }

        if (monitor is not null)
        {
            try
            {
                await monitor;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        // The monitor can outlive the tray message pump, so never resume it on the UI context.
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var result = Probe();
            var notify = false;
            lock (_gate)
            {
                if (_disposed || _selection is null)
                {
                    continue;
                }

                if (_lastFingerprint != result.Fingerprint)
                {
                    _lastFingerprint = result.Fingerprint;
                    notify = true;
                }
            }

            if (notify)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private ProbeResult Probe()
    {
        SourceDescriptor? selection;
        WindowTitleSettings settings;
        lock (_gate)
        {
            selection = _selection;
            settings = _settings;
        }

        if (selection is null)
        {
            return new ProbeResult(
                SessionObservation.Create(null, PlaybackState.Unavailable),
                SourceManagerState.Unconfigured,
                new ObservationFingerprint(
                    PlaybackState.Unavailable,
                    null,
                    null,
                    SourceStatusReason.Unconfigured));
        }

        try
        {
            var target = settings.Target;
            if (target is null
                || !string.Equals(target.InstanceId, selection.Key.InstanceId, StringComparison.Ordinal))
            {
                return Apply(ProbeResult.Unavailable(selection, SourceStatusReason.Missing));
            }

            var matches = _catalog.GetWindows()
                .Where(window => Matches(target, window.Target))
                .ToArray();
            if (matches.Length == 0)
            {
                return Apply(ProbeResult.Unavailable(selection, SourceStatusReason.Missing));
            }

            if (matches.Length > 1)
            {
                return Apply(ProbeResult.Unavailable(selection, SourceStatusReason.Ambiguous));
            }

            var parsed = WindowTitleParser.Parse(matches[0].Title, settings);
            if (!parsed.HasTrack)
            {
                return Apply(new ProbeResult(
                    SessionObservation.Create(selection, PlaybackState.Idle),
                    new SourceManagerState(selection, SourceStatus.Available, SourceStatusReason.None),
                    new ObservationFingerprint(PlaybackState.Idle, null, null, SourceStatusReason.None)));
            }

            var observation = SessionObservation.Create(
                selection,
                PlaybackState.Playing,
                TrackMetadata.Create(parsed.Title, parsed.Artist, albumTitle: null));
            return Apply(new ProbeResult(
                observation,
                new SourceManagerState(selection, SourceStatus.Available, SourceStatusReason.None),
                new ObservationFingerprint(
                    PlaybackState.Playing,
                    parsed.Title,
                    parsed.Artist,
                    SourceStatusReason.None)));
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            RecordFault(error, "polling");
            return Apply(new ProbeResult(
                SessionObservation.Create(selection, PlaybackState.Unavailable),
                new SourceManagerState(selection, SourceStatus.Faulted, SourceStatusReason.Faulted),
                new ObservationFingerprint(
                    PlaybackState.Unavailable,
                    null,
                    null,
                    SourceStatusReason.Faulted)));
        }
    }

    private ProbeResult Apply(ProbeResult result)
    {
        var recovered = false;
        lock (_gate)
        {
            if (!_disposed)
            {
                recovered = _faultLogged && result.State.Status != SourceStatus.Faulted;
                if (recovered)
                {
                    _faultLogged = false;
                }

                _state = result.State;
            }
        }

        if (recovered)
        {
            _logger.LogInformation("Window Title source recovered after a catalog failure.");
        }

        return result;
    }

    private void RecordFault(Exception error, string operation)
    {
        var log = false;
        lock (_gate)
        {
            if (!_faultLogged)
            {
                _faultLogged = true;
                log = true;
            }
        }

        if (log)
        {
            // Exception messages may contain a full title or executable path; keep only fault shape.
            _logger.LogError(
                "Window Title {Operation} failed. Error type {ErrorType}, HRESULT {ErrorHResult}.",
                operation,
                error.GetType().Name,
                error.HResult);
        }
    }

    private static bool Matches(
        WindowTitleTargetSettings selected,
        WindowTitleTargetSettings candidate)
    {
        return string.Equals(selected.ProcessName, candidate.ProcessName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(selected.WindowClass, candidate.WindowClass, StringComparison.Ordinal)
            && (selected.ExecutablePath is null
                || string.Equals(
                    selected.ExecutablePath,
                    candidate.ExecutablePath,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateSelection(SourceDescriptor? selection)
    {
        if (selection is not null && selection.Key.Provider != SourceProvider.WindowTitle)
        {
            throw new ArgumentException(
                "Window Title can only select a Window Title source.",
                nameof(selection));
        }
    }

    private sealed record ProbeResult(
        SessionObservation Observation,
        SourceManagerState State,
        ObservationFingerprint Fingerprint)
    {
        public static ProbeResult Unavailable(
            SourceDescriptor? source,
            SourceStatusReason reason)
        {
            var state = source is null
                ? SourceManagerState.Unconfigured
                : new SourceManagerState(source, SourceStatus.Unavailable, reason);
            return new ProbeResult(
                SessionObservation.Create(source, PlaybackState.Unavailable),
                state,
                new ObservationFingerprint(PlaybackState.Unavailable, null, null, reason));
        }
    }

    private sealed record ObservationFingerprint(
        PlaybackState Playback,
        string? Title,
        string? Artist,
        SourceStatusReason Reason);
}
