using System.Diagnostics;

namespace NowPlayingOverlay.Host.Media.Spotify.Authorization;

internal sealed class SpotifyAuthorizationService : IAsyncDisposable
{
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AccessTokenRefreshSkew = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SpotifyCredentialStore _credentialStore;
    private readonly SpotifyTokenClient _tokenClient;
    private readonly SpotifyAuthorizationCallbackBroker _callbackBroker;
    private readonly TimeProvider _timeProvider;
    private readonly Action<Uri> _openBrowser;
    private readonly HttpClient? _ownedHttpClient;
    private SpotifyClientId? _accessTokenClientId;
    private SpotifyAccessToken? _accessToken;
    private int _disposeStarted;

    public SpotifyAuthorizationService(
        string credentialFilePath,
        SpotifyAuthorizationCallbackBroker callbackBroker,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ownedHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _credentialStore = new SpotifyCredentialStore(credentialFilePath);
        _tokenClient = new SpotifyTokenClient(_ownedHttpClient, _timeProvider);
        _callbackBroker = callbackBroker
            ?? throw new ArgumentNullException(nameof(callbackBroker));
        _openBrowser = OpenSystemBrowser;
    }

    internal SpotifyAuthorizationService(
        SpotifyCredentialStore credentialStore,
        SpotifyTokenClient tokenClient,
        Action<Uri> openBrowser,
        SpotifyAuthorizationCallbackBroker? callbackBroker = null,
        TimeProvider? timeProvider = null)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _tokenClient = tokenClient ?? throw new ArgumentNullException(nameof(tokenClient));
        _openBrowser = openBrowser ?? throw new ArgumentNullException(nameof(openBrowser));
        _callbackBroker = callbackBroker ?? new SpotifyAuthorizationCallbackBroker();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public SpotifyConnectionState GetConnectionState(SpotifyClientId clientId)
    {
        ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
        SpotifyStoredCredential? credential;
        try
        {
            credential = _credentialStore.Load();
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            return new SpotifyConnectionState(SpotifyConnectionStatus.CredentialUnavailable);
        }

        if (credential is null)
        {
            return new SpotifyConnectionState(SpotifyConnectionStatus.Disconnected);
        }

        return credential.ClientId == clientId
            ? new SpotifyConnectionState(SpotifyConnectionStatus.Connected, credential.ClientId)
            : new SpotifyConnectionState(
                SpotifyConnectionStatus.ClientIdMismatch,
                credential.ClientId);
    }

    public Task<SpotifyConnectionState> ConnectAsync(
        SpotifyClientId clientId,
        Uri redirectUri,
        CancellationToken cancellationToken = default)
    {
        return AuthorizeAsync(clientId, redirectUri, cancellationToken);
    }

    public Task<SpotifyConnectionState> ReauthorizeAsync(
        SpotifyClientId clientId,
        Uri redirectUri,
        CancellationToken cancellationToken = default)
    {
        return AuthorizeAsync(clientId, redirectUri, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        await _operation.WaitAsync(operationCancellation.Token);
        try
        {
            _accessToken = null;
            _accessTokenClientId = null;
            _credentialStore.Delete();
        }
        finally
        {
            _operation.Release();
        }
    }

    public async Task<SpotifyAccessToken> GetAccessTokenAsync(
        SpotifyClientId clientId,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        await _operation.WaitAsync(operationCancellation.Token);
        try
        {
            if (!forceRefresh
                && _accessTokenClientId == clientId
                && _accessToken?.IsUsableAt(
                    _timeProvider.GetUtcNow(),
                    AccessTokenRefreshSkew) == true)
            {
                return _accessToken;
            }

            SpotifyStoredCredential credential;
            try
            {
                credential = _credentialStore.Load()
                    ?? throw new SpotifyReauthorizationRequiredException(
                        "Spotify is not connected.");
            }
            catch (SpotifyReauthorizationRequiredException)
            {
                throw;
            }
            catch (Exception error) when (error is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
            {
                throw new SpotifyReauthorizationRequiredException(
                    "The stored Spotify credential is unavailable.",
                    error);
            }

            if (credential.ClientId != clientId)
            {
                throw new SpotifyReauthorizationRequiredException(
                    "The stored Spotify credential belongs to a different Client ID.");
            }

            SpotifyTokenResult refreshed;
            try
            {
                refreshed = await _tokenClient.RefreshAsync(
                    clientId,
                    credential.RefreshToken,
                    operationCancellation.Token);
            }
            catch (SpotifyTokenRequestException error) when (error.RequiresReauthorization)
            {
                _accessToken = null;
                _accessTokenClientId = null;
                _credentialStore.Delete();
                throw new SpotifyReauthorizationRequiredException(
                    "The Spotify refresh token expired or was revoked.",
                    error);
            }

            var scope = refreshed.Scope ?? credential.Scope;
            if (!SpotifyAuthorizationRequest.HasRequiredScope(scope))
            {
                _accessToken = null;
                _accessTokenClientId = null;
                _credentialStore.Delete();
                throw new SpotifyReauthorizationRequiredException(
                    "Spotify no longer grants the required currently-playing scope.");
            }

            var refreshToken = refreshed.RefreshToken ?? credential.RefreshToken;
            if (!string.Equals(refreshToken, credential.RefreshToken, StringComparison.Ordinal)
                || !string.Equals(scope, credential.Scope, StringComparison.Ordinal))
            {
                _credentialStore.Save(new SpotifyStoredCredential(clientId, refreshToken, scope));
            }

            _accessToken = refreshed.AccessToken;
            _accessTokenClientId = clientId;
            return refreshed.AccessToken;
        }
        finally
        {
            _operation.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        await _operation.WaitAsync();
        try
        {
            _accessToken = null;
            _accessTokenClientId = null;
            _ownedHttpClient?.Dispose();
        }
        finally
        {
            _operation.Release();
        }

        _shutdown.Dispose();
        _operation.Dispose();
    }

    private async Task<SpotifyConnectionState> AuthorizeAsync(
        SpotifyClientId clientId,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        await _operation.WaitAsync(operationCancellation.Token);
        try
        {
            var request = SpotifyAuthorizationRequest.Create(clientId, redirectUri);
            using var callback = _callbackBroker.Begin(request.State);
            try
            {
                _openBrowser(request.AuthorizationUri);
            }
            catch (Exception error)
            {
                throw new SpotifyAuthorizationException(
                    "The system browser could not be opened for Spotify authorization.",
                    innerException: error);
            }

            var authorizationCode = await callback.WaitForAuthorizationCodeAsync(
                AuthorizationTimeout,
                operationCancellation.Token);
            var tokens = await _tokenClient.ExchangeAuthorizationCodeAsync(
                clientId,
                authorizationCode,
                request.RedirectUri,
                request.CodeVerifier,
                operationCancellation.Token);
            var credential = new SpotifyStoredCredential(
                clientId,
                tokens.RefreshToken!,
                tokens.Scope!);
            _credentialStore.Save(credential);
            _accessToken = tokens.AccessToken;
            _accessTokenClientId = clientId;
            return new SpotifyConnectionState(SpotifyConnectionStatus.Connected, clientId);
        }
        finally
        {
            _operation.Release();
        }
    }

    private static void OpenSystemBrowser(Uri uri)
    {
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
        if (process is null)
        {
            throw new InvalidOperationException("The system browser process did not start.");
        }
    }
}
