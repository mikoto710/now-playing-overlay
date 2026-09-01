using NowPlayingOverlay.Host.Hosting;
using NowPlayingOverlay.Host.Media.External;

namespace NowPlayingOverlay.Host.ControlPlane;

/// <summary>
/// Exports Browser Player connection codes and persists replacement keys before runtime transfer.
/// A failed transfer converges to the persisted key on restart.
/// </summary>
internal sealed class BrowserPlayerConnectionService
{
    private readonly IngestKeyStore _keyStore;
    private readonly ExternalIngestHttpHandler _ingestHandler;
    private readonly IOverlayHttpRuntime _httpServer;

    public BrowserPlayerConnectionService(
        IngestKeyStore keyStore,
        ExternalIngestHttpHandler ingestHandler,
        IOverlayHttpRuntime httpServer)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _ingestHandler = ingestHandler ?? throw new ArgumentNullException(nameof(ingestHandler));
        _httpServer = httpServer ?? throw new ArgumentNullException(nameof(httpServer));
    }

    public string GetConnectionCode()
    {
        return ExternalIngestConnectionCode.Create(
            _httpServer.CurrentPort,
            _ingestHandler.ExportKey());
    }

    public string RotateConnectionCode()
    {
        var replacement = _keyStore.Rotate();
        var transferred = false;
        try
        {
            _ingestHandler.ReplaceKey(replacement);
            transferred = true;
            return GetConnectionCode();
        }
        finally
        {
            if (!transferred)
            {
                replacement.Dispose();
            }
        }
    }

    internal string ExportKey()
    {
        return _ingestHandler.ExportKey();
    }
}
