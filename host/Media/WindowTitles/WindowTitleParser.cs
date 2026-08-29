using NowPlayingOverlay.Host.Configuration;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Media.WindowTitles;

internal static class WindowTitleParser
{
    public static WindowTitleParseResult Parse(string? rawTitle, WindowTitleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (settings.ParseMode == WindowTitleParseMode.WholeTitle)
        {
            var title = MediaTextNormalizer.Normalize(rawTitle);
            return title.Length == 0
                ? WindowTitleParseResult.NoTrack
                : new WindowTitleParseResult(title, string.Empty);
        }

        if (string.IsNullOrEmpty(rawTitle))
        {
            return WindowTitleParseResult.NoTrack;
        }

        var separatorIndex = settings.SplitOccurrence == WindowTitleSplitOccurrence.First
            ? rawTitle.IndexOf(settings.Separator, StringComparison.Ordinal)
            : rawTitle.LastIndexOf(settings.Separator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return WindowTitleParseResult.NoTrack;
        }

        var left = MediaTextNormalizer.Normalize(rawTitle[..separatorIndex]);
        var right = MediaTextNormalizer.Normalize(
            rawTitle[(separatorIndex + settings.Separator.Length)..]);
        if (left.Length == 0 || right.Length == 0)
        {
            return WindowTitleParseResult.NoTrack;
        }

        return settings.LeftField == WindowTitleField.Title
            ? new WindowTitleParseResult(left, right)
            : new WindowTitleParseResult(right, left);
    }
}

internal sealed record WindowTitleParseResult(string? Title, string? Artist)
{
    public static WindowTitleParseResult NoTrack { get; } = new(null, null);

    public bool HasTrack => Title is not null;
}
