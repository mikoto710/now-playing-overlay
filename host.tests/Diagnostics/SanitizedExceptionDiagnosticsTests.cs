using System.ComponentModel;
using NowPlayingOverlay.Host.Diagnostics;

namespace NowPlayingOverlay.Host.Tests.Diagnostics;

public sealed class SanitizedExceptionDiagnosticsTests
{
    [Fact]
    public void KeepsFaultShapeAndMethodChainWithoutMessagesOrPaths()
    {
        const string sensitive = @"Secret title C:\Private\Player.exe?token=secret";
        Exception error;
        try
        {
            ThrowNested(sensitive);
            throw new InvalidOperationException("Unreachable.");
        }
        catch (Exception caught)
        {
            error = caught;
        }

        var diagnostic = SanitizedExceptionDiagnostics.Create(error);

        Assert.Contains("Diagnostic ", diagnostic, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Win32Exception", diagnostic, StringComparison.Ordinal);
        Assert.Contains("NativeError=5", diagnostic, StringComparison.Ordinal);
        Assert.Contains(nameof(ThrowNested), diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitive, diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Private", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowNested(string sensitive)
    {
        try
        {
            throw new Win32Exception(5, sensitive);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(sensitive, error);
        }
    }
}
