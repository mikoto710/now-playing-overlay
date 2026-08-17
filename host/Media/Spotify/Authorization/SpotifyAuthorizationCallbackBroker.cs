using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace NowPlayingOverlay.Host.Media.Spotify.Authorization;

internal sealed class SpotifyAuthorizationCallbackBroker
{
    private readonly object _gate = new();
    private PendingAuthorization? _pending;

    public bool HasPendingAuthorization
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null && Volatile.Read(ref _pending.Completed) == 0;
            }
        }
    }

    public SpotifyAuthorizationCallbackRegistration Begin(string expectedState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);
        lock (_gate)
        {
            if (_pending is not null)
            {
                throw new InvalidOperationException("A Spotify authorization callback is already pending.");
            }

            var pending = new PendingAuthorization(expectedState);
            _pending = pending;
            return new SpotifyAuthorizationCallbackRegistration(this, pending);
        }
    }

    public bool TryComplete(
        Uri callbackUri,
        out SpotifyAuthorizationCallbackResponse response)
    {
        ArgumentNullException.ThrowIfNull(callbackUri);
        PendingAuthorization? pending;
        lock (_gate)
        {
            pending = _pending;
        }

        if (pending is null || Interlocked.Exchange(ref pending.Completed, 1) != 0)
        {
            response = SpotifyAuthorizationCallbackResponse.NotFound;
            return false;
        }

        try
        {
            var callback = ParseCallback(callbackUri);
            if (!FixedTimeEquals(pending.ExpectedState, callback.State))
            {
                throw new SpotifyAuthorizationException(
                    "Spotify authorization callback state did not match.");
            }

            if (callback.ErrorCode is not null)
            {
                throw new SpotifyAuthorizationException(
                    $"Spotify authorization failed with '{callback.ErrorCode}'.",
                    callback.ErrorCode);
            }

            if (string.IsNullOrWhiteSpace(callback.AuthorizationCode)
                || callback.AuthorizationCode.Length > 8192
                || callback.AuthorizationCode.Any(char.IsControl))
            {
                throw new SpotifyAuthorizationException(
                    "Spotify authorization callback did not contain a valid code.");
            }

            pending.Completion.TrySetResult(callback.AuthorizationCode);
            response = SpotifyAuthorizationCallbackResponse.Completed;
        }
        catch (SpotifyAuthorizationException error)
        {
            pending.Completion.TrySetException(error);
            response = SpotifyAuthorizationCallbackResponse.Failed;
        }

        return true;
    }

    private static CallbackResult ParseCallback(Uri callbackUri)
    {
        if (!callbackUri.IsAbsoluteUri
            || !string.Equals(callbackUri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || !string.Equals(callbackUri.Host, "127.0.0.1", StringComparison.Ordinal)
            || !string.Equals(
                callbackUri.AbsolutePath,
                SpotifyAuthorizationRequest.RedirectPath,
                StringComparison.Ordinal)
            || !string.IsNullOrEmpty(callbackUri.Fragment))
        {
            throw new SpotifyAuthorizationException("Spotify callback path was invalid.");
        }

        var parameters = ParseQuery(callbackUri.Query);
        parameters.TryGetValue("state", out var state);
        parameters.TryGetValue("code", out var code);
        parameters.TryGetValue("error", out var error);
        if (error is { Length: > 128 } || error?.Any(char.IsControl) == true)
        {
            throw new SpotifyAuthorizationException("Spotify callback error was invalid.");
        }

        return new CallbackResult(state, code, error);
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var encodedName = separator < 0 ? pair : pair[..separator];
            var encodedValue = separator < 0 ? string.Empty : pair[(separator + 1)..];
            string name;
            string value;
            try
            {
                name = Uri.UnescapeDataString(encodedName.Replace('+', ' '));
                value = Uri.UnescapeDataString(encodedValue.Replace('+', ' '));
            }
            catch (UriFormatException error)
            {
                throw new SpotifyAuthorizationException(
                    "Spotify callback query was invalid.",
                    innerException: error);
            }

            if (!result.TryAdd(name, value))
            {
                throw new SpotifyAuthorizationException(
                    "Spotify callback query contained duplicates.");
            }
        }

        return result;
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private void End(PendingAuthorization pending)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
            }
        }
    }

    internal sealed class PendingAuthorization(string expectedState)
    {
        public TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ExpectedState { get; } = expectedState;

        public int Completed;
    }

    internal sealed class SpotifyAuthorizationCallbackRegistration : IDisposable
    {
        private readonly SpotifyAuthorizationCallbackBroker _broker;
        private readonly PendingAuthorization _pending;
        private bool _disposed;

        internal SpotifyAuthorizationCallbackRegistration(
            SpotifyAuthorizationCallbackBroker broker,
            PendingAuthorization pending)
        {
            _broker = broker;
            _pending = pending;
        }

        public async Task<string> WaitForAuthorizationCodeAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            try
            {
                return await _pending.Completion.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException error)
            {
                throw new SpotifyAuthorizationException(
                    "Spotify authorization callback timed out.",
                    innerException: error);
            }
            finally
            {
                _broker.End(_pending);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _broker.End(_pending);
        }
    }

    private sealed record CallbackResult(
        string? State,
        string? AuthorizationCode,
        string? ErrorCode);
}

internal sealed record SpotifyAuthorizationCallbackResponse(
    HttpStatusCode StatusCode,
    string Message)
{
    public static SpotifyAuthorizationCallbackResponse Completed { get; } = new(
        HttpStatusCode.OK,
        "Spotify connection completed. You can close this browser tab and return to Now Playing Overlay.");

    public static SpotifyAuthorizationCallbackResponse Failed { get; } = new(
        HttpStatusCode.BadRequest,
        "Spotify connection was not completed. Return to Now Playing Overlay and try again.");

    public static SpotifyAuthorizationCallbackResponse NotFound { get; } = new(
        HttpStatusCode.NotFound,
        string.Empty);
}
