using System.Reflection;

namespace NowPlayingOverlay.Host.Hosting;

internal sealed class BrowserProducerAsset
{
    public const string Path = "/NowPlayingOverlay.user.js";
    internal const string ResourceName = "NowPlayingOverlay.Integrations.NowPlayingOverlay.user.js";
    private readonly byte[] _bytes;

    private BrowserProducerAsset(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new InvalidDataException($"The embedded browser Producer '{ResourceName}' is empty.");
        }

        _bytes = bytes;
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    internal static BrowserProducerAsset LoadEmbedded(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded browser Producer '{ResourceName}' is missing from {assembly.GetName().Name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new BrowserProducerAsset(buffer.ToArray());
    }
}
