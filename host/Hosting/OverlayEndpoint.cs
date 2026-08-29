using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Hosting;

internal static class OverlayEndpoint
{
    public const string PagePath = "/NowPlaying.html";

    public static string BuildUrl(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        return $"http://{HostOptions.AllowedHost}:{port}{PagePath}";
    }

    public static string BuildPreviewUrl(int port, int previewScale)
    {
        if (previewScale is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(previewScale));
        }

        return $"{BuildUrl(port)}?previewScale={previewScale}";
    }
}
