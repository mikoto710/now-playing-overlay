using System.Security.Cryptography;
using System.Text;

namespace NowPlayingOverlay.Host.Configuration;

internal enum WindowTitleParseMode
{
    WholeTitle,
    Split,
}

internal enum WindowTitleSplitOccurrence
{
    First,
    Last,
}

internal enum WindowTitleField
{
    Title,
    Artist,
}

internal sealed record WindowTitleSettings
{
    public const int MaximumSeparatorLength = 256;

    public WindowTitleTargetSettings? Target { get; init; }

    public WindowTitleParseMode ParseMode { get; init; } = WindowTitleParseMode.WholeTitle;

    public string Separator { get; init; } = " - ";

    public WindowTitleSplitOccurrence SplitOccurrence { get; init; } =
        WindowTitleSplitOccurrence.First;

    public WindowTitleField LeftField { get; init; } = WindowTitleField.Artist;

    public void Validate()
    {
        Target?.Validate();
        if (!Enum.IsDefined(ParseMode))
        {
            throw new InvalidDataException("The Window Title parse mode is invalid.");
        }

        if (!Enum.IsDefined(SplitOccurrence))
        {
            throw new InvalidDataException("The Window Title split occurrence is invalid.");
        }

        if (!Enum.IsDefined(LeftField))
        {
            throw new InvalidDataException("The Window Title left-side field is invalid.");
        }

        if (Separator is null)
        {
            throw new InvalidDataException("The Window Title separator must not be null.");
        }

        if (Separator.Length > MaximumSeparatorLength || Separator.Any(char.IsControl))
        {
            throw new InvalidDataException(
                $"The Window Title separator must be at most {MaximumSeparatorLength} non-control characters.");
        }

        if (ParseMode == WindowTitleParseMode.Split && Separator.Length == 0)
        {
            throw new InvalidDataException("Enter a separator before splitting a window title.");
        }
    }
}

internal sealed record WindowTitleTargetSettings
{
    public const int MaximumProcessNameLength = 260;
    public const int MaximumWindowClassLength = 512;
    public const int MaximumExecutablePathLength = 1024;

    public required string ProcessName { get; init; }

    public string? ExecutablePath { get; init; }

    public required string WindowClass { get; init; }

    public string InstanceId
    {
        get
        {
            Validate();
            var canonical = string.Join(
                '\n',
                ProcessName.ToUpperInvariant(),
                ExecutablePath?.ToUpperInvariant() ?? string.Empty,
                WindowClass);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
        }
    }

    public string DisplayName => ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        ? ProcessName
        : $"{ProcessName}.exe";

    public void Validate()
    {
        ValidateRequired(ProcessName, MaximumProcessNameLength, "process name");
        ValidateRequired(WindowClass, MaximumWindowClassLength, "window class");
        if (ExecutablePath is null)
        {
            return;
        }

        if (ExecutablePath.Length == 0
            || ExecutablePath.Length > MaximumExecutablePathLength
            || ExecutablePath.Any(char.IsControl)
            || !Path.IsPathFullyQualified(ExecutablePath))
        {
            throw new InvalidDataException(
                "The Window Title executable path must be an absolute non-control path.");
        }
    }

    private static void ValidateRequired(string? value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new InvalidDataException(
                $"The Window Title {name} must be at most {maximumLength} non-control characters.");
        }
    }
}
