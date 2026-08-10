using NowPlayingOverlay.Host.Shell;

namespace NowPlayingOverlay.Host.Tests.Shell;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void NamedMutexRejectsSecondOwnerAndCanBeReacquiredAfterRelease()
    {
        var name = $@"Local\NowPlayingOverlay.Tests.{Guid.NewGuid():N}";

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var first));
        Assert.False(SingleInstanceGuard.TryAcquire(name, out var second));
        Assert.Null(second);

        first!.Dispose();

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var third));
        third!.Dispose();
    }
}
