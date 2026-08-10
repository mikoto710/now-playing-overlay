using System.Reflection;

namespace NowPlayingOverlay.Host.Shell;

internal static class ApplicationIconProvider
{
    internal const string ResourceName = "NowPlayingOverlay.Assets.NowPlayingOverlay.ico";

    public static Icon LoadSmallIcon()
    {
        return Load(SystemInformation.SmallIconSize);
    }

    internal static Icon Load(Size size, Assembly? assembly = null)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Icon dimensions must be positive.");
        }

        assembly ??= typeof(ApplicationIconProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded application icon '{ResourceName}' is missing from {assembly.GetName().Name}.");
        using var source = new Icon(stream, size);
        // Icon retains its source stream, so clone before closing the embedded resource.
        return (Icon)source.Clone();
    }
}
