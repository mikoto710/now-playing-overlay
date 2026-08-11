using System.Reflection;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class OverlayPageAsset
{
    internal const string ResourceName = "NowPlayingOverlay.Web.NowPlaying.html";
    private readonly byte[] _bytes;

    private OverlayPageAsset(byte[] bytes, string source)
    {
        if (bytes.Length == 0)
        {
            throw new InvalidDataException($"The overlay page from '{source}' is empty.");
        }

        _bytes = bytes;
        Source = source;
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public string Source { get; }

    internal static OverlayPageAsset LoadEmbedded(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded overlay page '{ResourceName}' is missing from {assembly.GetName().Name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new OverlayPageAsset(buffer.ToArray(), $"embedded resource {ResourceName}");
    }

}
