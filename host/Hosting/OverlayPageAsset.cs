using System.Reflection;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class OverlayPageAsset
{
    internal const string ResourceName = "NowPlayingOverlay.Web.NowPlaying.html";
    private readonly byte[] _bytes;

    private OverlayPageAsset(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new InvalidDataException($"The embedded overlay page '{ResourceName}' is empty.");
        }

        _bytes = bytes;
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    internal static OverlayPageAsset LoadEmbedded(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded overlay page '{ResourceName}' is missing from {assembly.GetName().Name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new OverlayPageAsset(buffer.ToArray());
    }
}
