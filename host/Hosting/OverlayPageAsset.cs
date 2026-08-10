using System.Reflection;
using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Hosting;

using OverlayHostOptions = Configuration.HostOptions;

internal sealed class OverlayPageAsset
{
    internal const string ResourceName = "NowPlayingOverlay.Web.NowPlaying.html";
    private const string FileName = "NowPlaying.html";
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

    public static OverlayPageAsset Load(
        OverlayHostOptions options,
        string contentRootPath,
        Assembly? assembly = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        return options.WebAssetMode switch
        {
            WebAssetMode.Embedded => LoadEmbedded(assembly ?? typeof(OverlayPageAsset).Assembly),
            WebAssetMode.Development => LoadDevelopment(options, contentRootPath),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.WebAssetMode,
                "Unknown web asset mode."),
        };
    }

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

    private static OverlayPageAsset LoadDevelopment(
        OverlayHostOptions options,
        string contentRootPath)
    {
        var configuredRoot = options.DevelopmentWebRoot ?? Path.Combine("web", "dist");
        var webRoot = Path.IsPathRooted(configuredRoot)
            ? Path.GetFullPath(configuredRoot)
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredRoot));
        var pagePath = Path.Combine(webRoot, FileName);
        if (!File.Exists(pagePath))
        {
            throw new FileNotFoundException(
                $"The development overlay page is missing at '{pagePath}'. Run 'npm --prefix web run build' or configure Host:DevelopmentWebRoot.",
                pagePath);
        }

        return new OverlayPageAsset(File.ReadAllBytes(pagePath), pagePath);
    }
}
