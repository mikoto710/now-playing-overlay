using Microsoft.Extensions.Logging.Abstractions;
using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.ControlPlane;

internal sealed record SettingsDraft(
    int Port,
    SourceProvider Provider,
    string? InstanceId,
    AppearanceSettings Appearance,
    OutputSettings Outputs,
    WindowTitleSettings WindowTitle);

internal sealed record SettingsApplyResult(
    bool PortChanged,
    string OverlayUrl);

/// <summary>
/// Builds and validates one complete settings candidate, prepares the fallible port change,
/// persists once, then applies deterministic in-memory updates. Once persistence succeeds the
/// candidate is authoritative; an unexpected runtime apply fault requires a restart to converge.
/// </summary>
internal sealed class SettingsApplicationWorkflow
{
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly IOverlayHttpRuntime _httpServer;
    private readonly SpotifyAuthorizationService _spotifyAuthorization;
    private readonly MediaSourceService _sources;
    private readonly AppearanceState _appearance;
    private readonly OutputManager _outputs;
    private readonly ILogger<SettingsApplicationWorkflow> _logger;

    public SettingsApplicationWorkflow(
        ApplicationSettingsStore settingsStore,
        IOverlayHttpRuntime httpServer,
        SpotifyAuthorizationService spotifyAuthorization,
        MediaSourceService sources,
        AppearanceState appearance,
        OutputManager outputs,
        ILogger<SettingsApplicationWorkflow>? logger = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _httpServer = httpServer ?? throw new ArgumentNullException(nameof(httpServer));
        _spotifyAuthorization = spotifyAuthorization
            ?? throw new ArgumentNullException(nameof(spotifyAuthorization));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        _logger = logger ?? NullLogger<SettingsApplicationWorkflow>.Instance;
    }

    public ApplicationSettings GetCurrent()
    {
        return _settingsStore.Load().Settings;
    }

    public async Task<SettingsApplyResult> ApplyAsync(
        SettingsDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Appearance);
        ArgumentNullException.ThrowIfNull(draft.Outputs);
        ArgumentNullException.ThrowIfNull(draft.WindowTitle);

        var current = GetCurrent();
        var candidate = BuildCandidate(current, draft);
        candidate.Validate();
        ValidateSpotifySelection(candidate);

        var portChanged = candidate.Port != _httpServer.CurrentPort;
        if (portChanged)
        {
            // Rebind starts the candidate listener before this callback persists the candidate.
            await _httpServer.RebindAsync(
                candidate.Port,
                () => _settingsStore.Save(candidate),
                cancellationToken);
        }
        else
        {
            _settingsStore.Save(candidate);
        }

        try
        {
            ApplyRuntimeSettings(candidate);
        }
        catch (Exception error)
        {
            // Settings may contain media text, templates, and paths; log only sanitized fault data.
            _logger.LogError(
                "Persisted settings could not be fully applied at runtime. Error type {ErrorType}, HRESULT {ErrorHResult}.",
                error.GetType().Name,
                error.HResult);
            throw new InvalidOperationException(
                "Settings were saved, but the running application could not apply them completely. Restart Now Playing Overlay to load the persisted settings.");
        }

        return new SettingsApplyResult(
            portChanged,
            OverlayEndpoint.BuildUrl(_httpServer.CurrentPort));
    }

    private static ApplicationSettings BuildCandidate(
        ApplicationSettings current,
        SettingsDraft draft)
    {
        var source = new SourceSelectionSettings
        {
            Provider = draft.Provider,
            InstanceId = draft.InstanceId,
        };
        var windowsMedia = draft.Provider == SourceProvider.WindowsMedia
            ? current.WindowsMedia with { LastInstanceId = draft.InstanceId }
            : current.WindowsMedia;
        return current with
        {
            Port = draft.Port,
            Source = source,
            WindowsMedia = windowsMedia,
            Appearance = draft.Appearance,
            Outputs = draft.Outputs,
            WindowTitle = draft.WindowTitle,
        };
    }

    private void ValidateSpotifySelection(ApplicationSettings candidate)
    {
        if (candidate.Source.Provider != SourceProvider.SpotifyApi)
        {
            return;
        }

        var clientId = candidate.Spotify.ToClientId()
            ?? throw new InvalidDataException("Connect Spotify before selecting Spotify API.");
        if (_spotifyAuthorization.GetConnectionState(clientId).Status
            != SpotifyConnectionStatus.Connected)
        {
            throw new InvalidDataException("Reconnect Spotify before selecting Spotify API.");
        }
    }

    private void ApplyRuntimeSettings(ApplicationSettings candidate)
    {
        _sources.UpdateWindowTitleSettings(candidate.WindowTitle);
        if (!Equals(
                _sources.GetState().ActiveSource?.Key,
                candidate.Source.ToDescriptor()?.Key))
        {
            _sources.Select(candidate.Source);
        }

        _appearance.Set(candidate.Appearance);
        _outputs.UpdateSettings(candidate.Outputs);
    }
}
