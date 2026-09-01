using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;
using NowPlayingOverlay.Host.Media.Spotify.Playback;
using NowPlayingOverlay.Host.Media.Windows;
using NowPlayingOverlay.Host.Media.WindowTitles;

namespace NowPlayingOverlay.Host.ControlPlane;

/// <summary>
/// Provides provider-specific discovery and selection workflows used by Settings. Provider
/// lifetimes remain owned by <see cref="ActiveSourceManager"/> and the runtime.
/// </summary>
internal sealed class MediaSourceService
{
    private readonly ActiveSourceManager _activeSources;
    private readonly WindowsMediaSource _windowsMedia;
    private readonly SpotifyApiSource _spotify;
    private readonly WindowTitleSource _windowTitle;

    public MediaSourceService(
        ActiveSourceManager activeSources,
        WindowsMediaSource windowsMedia,
        SpotifyApiSource spotify,
        WindowTitleSource windowTitle)
    {
        _activeSources = activeSources ?? throw new ArgumentNullException(nameof(activeSources));
        _windowsMedia = windowsMedia ?? throw new ArgumentNullException(nameof(windowsMedia));
        _spotify = spotify ?? throw new ArgumentNullException(nameof(spotify));
        _windowTitle = windowTitle ?? throw new ArgumentNullException(nameof(windowTitle));
    }

    public SourceManagerState GetState()
    {
        return _activeSources.GetState();
    }

    public async Task<SourceDiscoveryResult> RefreshAsync(
        SourceProvider provider,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return provider switch
        {
            SourceProvider.WindowsMedia => await _windowsMedia.RefreshSourcesAsync(cancellationToken),
            SourceProvider.SpotifyApi => new SourceDiscoveryResult(
                [SourceDescriptor.SpotifyApi()],
                GetState()),
            SourceProvider.ExternalPush => new SourceDiscoveryResult(
                [SourceDescriptor.ExternalPush()],
                GetState()),
            SourceProvider.WindowTitle => await RefreshWindowTitleDescriptorsAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }

    public Task<WindowTitleDiscoveryResult> RefreshWindowTitlesAsync(
        CancellationToken cancellationToken = default)
    {
        return _windowTitle.RefreshSourcesAsync(cancellationToken);
    }

    public void Select(SourceSelectionSettings selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        _activeSources.Select(selection.ToDescriptor());
    }

    public void SetSpotifyClientId(SpotifyClientId? clientId)
    {
        _spotify.SetClientId(clientId);
    }

    public void UpdateWindowTitleSettings(WindowTitleSettings settings)
    {
        _windowTitle.UpdateSettings(settings);
    }

    private async Task<SourceDiscoveryResult> RefreshWindowTitleDescriptorsAsync(
        CancellationToken cancellationToken)
    {
        var discovery = await RefreshWindowTitlesAsync(cancellationToken);
        return new SourceDiscoveryResult(
            discovery.Candidates.Select(candidate => candidate.ToDescriptor()).ToArray(),
            discovery.State);
    }
}
