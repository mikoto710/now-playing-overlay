using System.Security.Cryptography;
using System.Text;

namespace NowPlayingOverlay.Host.Media.Spotify.Authorization;

internal sealed record SpotifyAuthorizationRequest
{
    public const string RequiredScope = "user-read-currently-playing";
    public const string RedirectPath = "/oauth/spotify/callback";

    private static readonly Uri AuthorizationEndpoint = new("https://accounts.spotify.com/authorize");

    private SpotifyAuthorizationRequest(
        Uri authorizationUri,
        Uri redirectUri,
        string state,
        string codeVerifier)
    {
        AuthorizationUri = authorizationUri;
        RedirectUri = redirectUri;
        State = state;
        CodeVerifier = codeVerifier;
    }

    public Uri AuthorizationUri { get; }

    public Uri RedirectUri { get; }

    public string State { get; }

    public string CodeVerifier { get; }

    public static Uri CreateLoopbackRedirectUri(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        return new Uri($"http://127.0.0.1:{port}{RedirectPath}");
    }

    public static SpotifyAuthorizationRequest Create(
        SpotifyClientId clientId,
        Uri redirectUri)
    {
        return Create(
            clientId,
            redirectUri,
            ToBase64Url(RandomNumberGenerator.GetBytes(64)),
            ToBase64Url(RandomNumberGenerator.GetBytes(32)));
    }

    internal static SpotifyAuthorizationRequest Create(
        SpotifyClientId clientId,
        Uri redirectUri,
        string codeVerifier,
        string state)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        ValidateRedirectUri(redirectUri);
        ValidateCodeVerifier(codeVerifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        var challenge = ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var query = string.Join(
            "&",
            new Dictionary<string, string>
            {
                ["client_id"] = clientId.Value,
                ["response_type"] = "code",
                ["redirect_uri"] = redirectUri.AbsoluteUri,
                ["scope"] = RequiredScope,
                ["state"] = state,
                ["code_challenge_method"] = "S256",
                ["code_challenge"] = challenge,
            }.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var builder = new UriBuilder(AuthorizationEndpoint) { Query = query };
        return new SpotifyAuthorizationRequest(builder.Uri, redirectUri, state, codeVerifier);
    }

    public static bool HasRequiredScope(string? scope)
    {
        return scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(RequiredScope, StringComparer.Ordinal) == true;
    }

    private static void ValidateRedirectUri(Uri redirectUri)
    {
        if (!redirectUri.IsAbsoluteUri
            || !string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || !string.Equals(redirectUri.Host, "127.0.0.1", StringComparison.Ordinal)
            || redirectUri.Port is < 1 or > 65535
            || !string.Equals(redirectUri.AbsolutePath, RedirectPath, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(redirectUri.Query)
            || !string.IsNullOrEmpty(redirectUri.Fragment))
        {
            throw new ArgumentException(
                "Spotify redirect URI must be an exact HTTP 127.0.0.1 loopback callback.",
                nameof(redirectUri));
        }
    }

    private static void ValidateCodeVerifier(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        if (codeVerifier.Length is < 43 or > 128
            || codeVerifier.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '.' and not '_' and not '~'))
        {
            throw new ArgumentException("Spotify PKCE code verifier is invalid.", nameof(codeVerifier));
        }
    }

    private static string ToBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
