using System.Windows.Forms;

namespace NowPlayingOverlay.Host.Shell;

internal static class ApplicationIconProvider
{
    private const string ResourceName = "NowPlayingOverlay.Assets.NowPlayingOverlay.ico";

    public static Icon LoadSmallIcon()
    {
        var assembly = typeof(ApplicationIconProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded application icon '{ResourceName}' is missing from {assembly.GetName().Name}.");
        using var source = new Icon(stream, SystemInformation.SmallIconSize);
        // Icon retains its source stream, so clone before closing the embedded resource.
        return (Icon)source.Clone();
    }
}
