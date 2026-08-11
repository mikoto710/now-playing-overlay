using Windows.Storage.Streams;
using NowPlayingOverlay.Host.Media.Windows;

namespace NowPlayingOverlay.Host.Tests.Media.Windows;

public sealed class WindowsArtworkReaderTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] OnePixelGif = Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");

    [Fact]
    public async Task ReadsOnlyAfterSuccessfulAllowedDecoderValidation()
    {
        using var stream = await CreateStreamAsync(OnePixelPng);
        var reader = CreateReader(stream);

        var payload = await reader.ReadAsync(CancellationToken.None);

        Assert.NotNull(payload);
        Assert.Equal(OnePixelPng, payload.Bytes.ToArray());
    }

    [Fact]
    public async Task RejectsDecodedFormatsOutsideTheAllowlist()
    {
        using var stream = await CreateStreamAsync(OnePixelGif);
        var reader = CreateReader(stream);

        var payload = await reader.ReadAsync(CancellationToken.None);

        Assert.Null(payload);
    }

    [Fact]
    public async Task RejectsReportedSizeBeforeDecodeOrAllocation()
    {
        var oversized = new byte[5 * 1024 * 1024 + 1];
        using var stream = await CreateStreamAsync(oversized);
        var reader = CreateReader(stream);

        var payload = await reader.ReadAsync(CancellationToken.None);

        Assert.Null(payload);
    }

    [Fact]
    public async Task HonorsCancellationBeforeOpeningStream()
    {
        using var stream = await CreateStreamAsync(OnePixelPng);
        var reader = CreateReader(stream);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.ReadAsync(cancellation.Token));
    }

    private static async Task<InMemoryRandomAccessStream> CreateStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        stream.Seek(0);
        return stream;
    }

    private static WindowsArtworkReader CreateReader(IRandomAccessStream stream)
    {
        return new WindowsArtworkReader(() => ValueTask.FromResult(stream));
    }
}
