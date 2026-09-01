using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Media.Spotify.Authorization;

namespace NowPlayingOverlay.Host.ControlPlane;

/// <summary>
/// Owns immediate Spotify authorization operations. Successful authorization is persisted at
/// once and is intentionally independent from the Settings dialog Save/Cancel lifecycle.
/// </summary>
internal sealed class SpotifyConnectionWorkflow
{
    private readonly SpotifyAuthorizationService _authorization;
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly MediaSourceService _sources;
    private readonly IOverlayHttpRuntime _httpServer;

    public SpotifyConnectionWorkflow(
        SpotifyAuthorizationService authorization,
        ApplicationSettingsStore settingsStore,
        MediaSourceService sources,
        IOverlayHttpRuntime httpServer)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _httpServer = httpServer ?? throw new ArgumentNullException(nameof(httpServer));
    }

    public string? GetSavedClientId()
    {
        return _settingsStore.Load().Settings.Spotify.ClientId;
    }

    public SpotifyConnectionState GetConnectionState(SpotifyClientId clientId)
    {
        return _authorization.GetConnectionState(clientId);
    }

    public async Task<SpotifyConnectionState> AuthorizeAsync(
        SpotifyClientId clientId,
        bool reauthorize,
        CancellationToken cancellationToken = default)
    {
        var redirectUri = SpotifyAuthorizationRequest.CreateLoopbackRedirectUri(
            _httpServer.CurrentPort);
        var state = reauthorize
            ? await _authorization.ReauthorizeAsync(clientId, redirectUri, cancellationToken)
            : await _authorization.ConnectAsync(clientId, redirectUri, cancellationToken);
        if (state.Status != SpotifyConnectionStatus.Connected)
        {
            return state;
        }

        _settingsStore.Update(current => current with
        {
            Spotify = new SpotifyConnectionSettings { ClientId = clientId.Value },
        });
        _sources.SetSpotifyClientId(clientId);
        return state;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _authorization.DisconnectAsync(cancellationToken);
        _sources.SetSpotifyClientId(null);

        SourceSelectionSettings? fallback = null;
        _settingsStore.Update(current =>
        {
            var source = current.Source;
            if (source.Provider == SourceProvider.SpotifyApi)
            {
                source = SourceSelectionSettings.WindowsMedia(
                    current.WindowsMedia.LastInstanceId);
                fallback = source;
            }

            return current with
            {
                Source = source,
                Spotify = new SpotifyConnectionSettings(),
            };
        });

        if (fallback is not null)
        {
            _sources.Select(fallback);
        }
    }
}
