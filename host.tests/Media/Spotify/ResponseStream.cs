namespace NowPlayingOverlay.Host.Tests.Media.Spotify;

/// <summary>A network response with no declared length and controllable asynchronous reads.</summary>
internal sealed class ResponseStream(Func<Memory<byte>, CancellationToken, ValueTask<int>> read) : Stream
{
    public static ResponseStream Stalled(TaskCompletionSource<CancellationToken> started) =>
        new(async (_, token) =>
        {
            started.SetResult(token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        });

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        read(buffer, cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
