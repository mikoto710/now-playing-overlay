using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Artwork;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Diagnostics;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Models;
using NowPlayingOverlay.Host.Protocol;
using NowPlayingOverlay.Host.State;

namespace NowPlayingOverlay.Host.Outputs;

/// <summary>
/// Runs latest-wins current outputs and an ordered, overflow-faulting History worker.
/// </summary>
internal sealed class OutputManager : IOutputRuntime
{
    internal const int HistoryQueueCapacity = 256;

    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly object _settingsGate = new(); // Protects settings and their generation.
    private readonly object _statusGate = new(); // Protects user-visible target status.
    private readonly NowPlayingStore _store;
    private readonly ArtworkCache _artworkCache;
    private readonly AtomicOutputFile _atomicFile;
    private readonly ArtworkPngEncoder _artworkEncoder;
    private readonly ILogger<OutputManager> _logger;
    private readonly Channel<NowPlayingSnapshot> _latestSignals = Channel.CreateBounded<NowPlayingSnapshot>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly Dictionary<string, string> _writtenContentHashes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _writtenArtworkIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OutputTargetStatus> _targetStatuses =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, OutputTargetStatus> _workerStatuses =
        new(StringComparer.Ordinal);
    private OutputSettings _settings;
    private long _historyWriteAfterRevision;
    private long _settingsGeneration; // Rejects status updates from old settings.
    private CancellationTokenSource? _shutdown;
    private NowPlayingSubscription? _latestSubscription;
    private OrderedNowPlayingSubscription? _historySubscription;
    private Task? _latestPump;
    private Task? _latestWorker;
    private Task? _historyWorker;
    private bool _started;
    private bool _stopped;
    private bool _disposed;

    public OutputManager(
        NowPlayingStore store,
        ArtworkCache artworkCache,
        OutputSettings settings,
        AtomicOutputFile? atomicFile = null,
        ArtworkPngEncoder? artworkEncoder = null,
        ILogger<OutputManager>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _artworkCache = artworkCache ?? throw new ArgumentNullException(nameof(artworkCache));
        _atomicFile = atomicFile ?? new AtomicOutputFile();
        _artworkEncoder = artworkEncoder ?? new ArtworkPngEncoder();
        _logger = logger ?? NullLogger<OutputManager>.Instance;
        settings.Validate();
        _settings = CloneSettings(settings);
        _historyWriteAfterRevision = store.Current.SnapshotRevision;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Outputs have already started.");
        }

        if (_stopped)
        {
            throw new InvalidOperationException("Stopped output workers cannot be restarted.");
        }

        _shutdown = new CancellationTokenSource();
        _latestSubscription = _store.Subscribe();
        _historySubscription = _store.SubscribeOrdered(HistoryQueueCapacity);
        _latestPump = PumpLatestAsync(_shutdown.Token);
        _latestWorker = ProcessLatestAsync(_shutdown.Token);
        _historyWorker = ProcessHistoryAsync(_shutdown.Token);
        ObserveWorkerFault(
            _latestPump,
            "current-subscription",
            "Current outputs stopped because their snapshot subscription failed.");
        ObserveWorkerFault(
            _latestWorker,
            "current-worker",
            "Current outputs stopped because their worker failed unexpectedly.");
        ObserveWorkerFault(
            _historyWorker,
            "history-worker",
            "History stopped because its worker failed unexpectedly.");
        _started = true;
    }

    public void UpdateSettings(OutputSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        lock (_settingsGate)
        {
            _settings = CloneSettings(settings);
            _historyWriteAfterRevision = _store.Current.SnapshotRevision;
            _settingsGeneration = checked(_settingsGeneration + 1);
            lock (_statusGate)
            {
                _targetStatuses.Clear();
            }
        }

        // Rebuild current-state outputs immediately. History observes only future commits.
        _latestSignals.Writer.TryWrite(_store.Current);
    }

    public OutputStatusSnapshot GetStatus()
    {
        lock (_statusGate)
        {
            var faulted = _targetStatuses.Values.Count(status => status.IsFaulted)
                + _workerStatuses.Values.Count(status => status.IsFaulted);
            return faulted == 0
                ? new OutputStatusSnapshot(0, "Outputs are ready. No output errors are recorded.")
                : new OutputStatusSnapshot(
                    faulted,
                    $"{faulted} output target(s) need attention. Open the logs for details.");
        }
    }

    public string RenderPreview(string template)
    {
        return OutputTemplate.Parse(template, allowLineBreaks: true).Render(_store.Current);
    }

    public async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        if (!_started)
        {
            return;
        }

        _started = false;
        _shutdown!.Cancel();
        _latestSubscription!.Dispose();
        _historySubscription!.Dispose();
        _latestSignals.Writer.TryComplete();
        Exception? workerError = null;
        try
        {
            await Task.WhenAll(
                AwaitWorkerAsync(_latestPump!),
                AwaitWorkerAsync(_latestWorker!),
                AwaitWorkerAsync(_historyWorker!));
        }
        catch (Exception error)
        {
            workerError = error;
        }
        finally
        {
            _shutdown.Dispose();
            _shutdown = null;
            _latestSubscription = null;
            _historySubscription = null;
            _latestPump = null;
            _latestWorker = null;
            _historyWorker = null;
        }

        if (workerError is not null)
        {
            ExceptionDispatchInfo.Capture(workerError).Throw();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await StopAsync();
        }
        finally
        {
            _disposed = true;
        }
    }

    private async Task PumpLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var snapshot in _latestSubscription!.Reader.ReadAllAsync(cancellationToken))
            {
                _latestSignals.Writer.TryWrite(snapshot);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessLatestAsync(CancellationToken cancellationToken)
    {
        long lastProcessedRevision = -1;
        try
        {
            await foreach (var snapshot in _latestSignals.Reader.ReadAllAsync(cancellationToken))
            {
                if (snapshot.SnapshotRevision < lastProcessedRevision)
                {
                    continue;
                }

                lastProcessedRevision = snapshot.SnapshotRevision;
                var settings = GetSettings(out var settingsGeneration);
                await WriteTextOutputAsync(
                    settings.Text,
                    snapshot,
                    settingsGeneration,
                    cancellationToken);
                await WriteJsonOutputAsync(
                    settings.Json,
                    snapshot,
                    settingsGeneration,
                    cancellationToken);
                await WriteArtworkOutputAsync(
                    settings.Artwork,
                    snapshot,
                    settingsGeneration,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessHistoryAsync(CancellationToken cancellationToken)
    {
        TrackIdentity? lastObservedIdentity = null;
        try
        {
            await foreach (var snapshot in _historySubscription!.Reader.ReadAllAsync(cancellationToken))
            {
                if (snapshot.Identity is null)
                {
                    continue;
                }

                if (Equals(lastObservedIdentity, snapshot.Identity))
                {
                    continue;
                }

                lastObservedIdentity = snapshot.Identity;
                var history = GetHistorySettings(
                    out var writeAfterRevision,
                    out var settingsGeneration);
                if (!history.Enabled || snapshot.SnapshotRevision <= writeAfterRevision)
                {
                    continue;
                }

                try
                {
                    var rendered = OutputTemplate.Parse(
                        history.Template,
                        allowLineBreaks: false).Render(snapshot);
                    await AppendHistoryAsync(history.FilePath!, rendered, cancellationToken);
                    SetHealthy("history", "History is up to date.", settingsGeneration);
                }
                catch (Exception error) when (IsOutputFailure(error, cancellationToken))
                {
                    SetFault(
                        "history",
                        "History could not be appended.",
                        error,
                        settingsGeneration);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException error)
        {
            SetWorkerFault(
                "history-worker",
                "History stopped because its ordered commit queue faulted.",
                error);
        }
    }

    private async Task WriteTextOutputAsync(
        TextOutputSettings output,
        NowPlayingSnapshot snapshot,
        long settingsGeneration,
        CancellationToken cancellationToken)
    {
        if (!output.Enabled)
        {
            return;
        }

        try
        {
            string? rendered;
            if (snapshot.Track is not null)
            {
                rendered = OutputTemplate.Parse(
                    output.Template,
                    allowLineBreaks: true).Render(snapshot);
            }
            else
            {
                rendered = output.NoMediaBehavior switch
                {
                    NoMediaOutputBehavior.Clear => string.Empty,
                    NoMediaOutputBehavior.Placeholder => OutputTemplate.Parse(
                        output.NoMediaTemplate,
                        allowLineBreaks: true).Render(snapshot),
                    NoMediaOutputBehavior.KeepLast => null,
                    _ => throw new InvalidOperationException(
                        "The text output no-media behavior is invalid."),
                };
            }

            if (rendered is not null)
            {
                await WriteIfChangedAsync(
                    output.FilePath!,
                    Utf8WithoutBom.GetBytes(rendered),
                    cancellationToken);
            }

            SetHealthy("text", "Text output is up to date.", settingsGeneration);
        }
        catch (Exception error) when (IsOutputFailure(error, cancellationToken))
        {
            SetFault(
                "text",
                "Text output could not be written.",
                error,
                settingsGeneration);
        }
    }

    private async Task WriteJsonOutputAsync(
        JsonOutputSettings output,
        NowPlayingSnapshot snapshot,
        long settingsGeneration,
        CancellationToken cancellationToken)
    {
        if (!output.Enabled)
        {
            return;
        }

        try
        {
            var dto = NowPlayingStateMapper.Map(snapshot);
            var json = ProtocolJson.Serialize(
                dto,
                indented: output.Format == JsonOutputFormat.Indented);
            await WriteIfChangedAsync(
                output.FilePath!,
                Utf8WithoutBom.GetBytes(json),
                cancellationToken);
            SetHealthy("json", "JSON output is up to date.", settingsGeneration);
        }
        catch (Exception error) when (IsOutputFailure(error, cancellationToken))
        {
            SetFault(
                "json",
                "JSON output could not be written.",
                error,
                settingsGeneration);
        }
    }

    private async Task WriteArtworkOutputAsync(
        ArtworkOutputSettings output,
        NowPlayingSnapshot snapshot,
        long settingsGeneration,
        CancellationToken cancellationToken)
    {
        if (!output.Enabled)
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(output.FilePath!);
            if (snapshot.Artwork is null)
            {
                if (output.MissingArtworkBehavior == MissingArtworkBehavior.Delete)
                {
                    File.Delete(fullPath);
                    _writtenContentHashes.Remove(fullPath);
                    _writtenArtworkIds.Remove(fullPath);
                }

                SetHealthy(
                    "artwork",
                    "Artwork output reflects the current no-artwork state.",
                    settingsGeneration);
                return;
            }

            if (_writtenArtworkIds.TryGetValue(fullPath, out var previousArtworkId)
                && string.Equals(
                    previousArtworkId,
                    snapshot.Artwork.ArtworkId,
                    StringComparison.Ordinal)
                && File.Exists(fullPath))
            {
                SetHealthy(
                    "artwork",
                    "Artwork output is up to date.",
                    settingsGeneration);
                return;
            }

            if (!_artworkCache.TryGet(snapshot.Artwork.ArtworkId, out var entry))
            {
                throw new InvalidOperationException(
                    "The committed artwork is not available in the artwork cache.");
            }

            var png = await _artworkEncoder.EncodeAsync(entry!, cancellationToken);
            await WriteIfChangedAsync(output.FilePath!, png, cancellationToken);
            _writtenArtworkIds[fullPath] = snapshot.Artwork.ArtworkId;
            SetHealthy(
                "artwork",
                "Artwork output is up to date.",
                settingsGeneration);
        }
        catch (Exception error) when (IsOutputFailure(error, cancellationToken))
        {
            SetFault(
                "artwork",
                "Artwork output could not be written.",
                error,
                settingsGeneration);
        }
    }

    private async Task WriteIfChangedAsync(
        string filePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        var hash = Convert.ToHexString(SHA256.HashData(content.Span));
        if (_writtenContentHashes.TryGetValue(fullPath, out var previous)
            && string.Equals(previous, hash, StringComparison.Ordinal)
            && File.Exists(fullPath))
        {
            return;
        }

        await _atomicFile.WriteBytesAsync(fullPath, content, cancellationToken);
        _writtenContentHashes[fullPath] = hash;
    }

    private static async Task AppendHistoryAsync(
        string filePath,
        string rendered,
        CancellationToken cancellationToken)
    {
        var bytes = Utf8WithoutBom.GetBytes(rendered + Environment.NewLine);
        await using var stream = new FileStream(
            Path.GetFullPath(filePath),
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private OutputSettings GetSettings(out long settingsGeneration)
    {
        lock (_settingsGate)
        {
            settingsGeneration = _settingsGeneration;
            return _settings;
        }
    }

    private HistoryOutputSettings GetHistorySettings(
        out long writeAfterRevision,
        out long settingsGeneration)
    {
        lock (_settingsGate)
        {
            writeAfterRevision = _historyWriteAfterRevision;
            settingsGeneration = _settingsGeneration;
            return _settings.History;
        }
    }

    private static OutputSettings CloneSettings(OutputSettings settings)
    {
        return settings with { Text = settings.Text with { } };
    }

    private void SetHealthy(string key, string message, long settingsGeneration)
    {
        lock (_settingsGate)
        {
            if (settingsGeneration != _settingsGeneration)
            {
                return;
            }

            lock (_statusGate)
            {
                _targetStatuses[key] = new OutputTargetStatus(
                    IsFaulted: false,
                    message,
                    DateTimeOffset.UtcNow);
            }
        }
    }

    private void SetFault(
        string key,
        string message,
        Exception error,
        long settingsGeneration)
    {
        bool shouldLog;
        lock (_settingsGate)
        {
            if (settingsGeneration != _settingsGeneration)
            {
                return;
            }

            lock (_statusGate)
            {
                shouldLog = !_targetStatuses.TryGetValue(key, out var previous)
                    || !previous.IsFaulted
                    || !string.Equals(previous.Message, message, StringComparison.Ordinal);
                _targetStatuses[key] = new OutputTargetStatus(
                    IsFaulted: true,
                    message,
                    DateTimeOffset.UtcNow);
            }
        }

        if (!shouldLog)
        {
            return;
        }

        // Target paths and rendered media text are intentionally omitted, including exception text.
        _logger.LogError(
            "{OutputMessage} {Diagnostic}",
            message,
            SanitizedExceptionDiagnostics.Create(error));
    }

    private static bool IsOutputFailure(Exception error, CancellationToken cancellationToken)
    {
        return (error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or FormatException
            or System.Runtime.InteropServices.ExternalException
            or NotSupportedException
            or ArgumentException)
            || error is OperationCanceledException && !cancellationToken.IsCancellationRequested;
    }

    private static async Task AwaitWorkerAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ObserveWorkerFault(Task task, string key, string message)
    {
        _ = task.ContinueWith(
            completed =>
            {
                SetWorkerFault(
                    key,
                    message,
                    completed.Exception!.GetBaseException());
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void SetWorkerFault(string key, string message, Exception error)
    {
        bool shouldLog;
        lock (_statusGate)
        {
            shouldLog = !_workerStatuses.TryGetValue(key, out var previous)
                || !previous.IsFaulted
                || !string.Equals(previous.Message, message, StringComparison.Ordinal);
            _workerStatuses[key] = new OutputTargetStatus(
                IsFaulted: true,
                message,
                DateTimeOffset.UtcNow);
        }

        if (shouldLog)
        {
            _logger.LogError(
                "{OutputMessage} {Diagnostic}",
                message,
                SanitizedExceptionDiagnostics.Create(error));
        }
    }
}
