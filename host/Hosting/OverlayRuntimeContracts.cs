namespace NowPlayingOverlay.Host.Hosting;

/// <summary>
/// Owns the loopback listener lifecycle and the transactional port-rebind boundary.
/// </summary>
internal interface IOverlayHttpRuntime : IAsyncDisposable
{
    int CurrentPort { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task RebindAsync(
        int newPort,
        Action persistPort,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the single serialized Source-to-Store processing pipeline.
/// </summary>
internal interface INowPlayingRuntime : IAsyncDisposable
{
    void Start();
}

/// <summary>
/// Owns the latest-value current outputs and ordered History workers.
/// </summary>
internal interface IOutputRuntime : IAsyncDisposable
{
    void Start();

    Task StopAsync();
}
