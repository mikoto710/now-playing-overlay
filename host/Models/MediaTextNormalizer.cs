using System.Text;

namespace NowPlayingOverlay.Host.Models;

internal static class MediaTextNormalizer
{
    public const int MaximumScalarCount = 512;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = new StringBuilder(value.Length);
        var previousWasLineBreak = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var isLineBreak = rune.Value is '\r' or '\n' or 0x0085 or 0x2028 or 0x2029;
            if (isLineBreak)
            {
                // Treat CRLF and other consecutive separators as one logical line break.
                if (!previousWasLineBreak)
                {
                    sanitized.Append(' ');
                }
            }
            else
            {
                sanitized.Append(rune.ToString());
            }

            previousWasLineBreak = isLineBreak;
        }

        var normalized = sanitized.ToString().Normalize(NormalizationForm.FormC).Trim();
        if (normalized.EnumerateRunes().Take(MaximumScalarCount + 1).Count() <= MaximumScalarCount)
        {
            return normalized;
        }

        var truncated = new StringBuilder(normalized.Length);
        foreach (var rune in normalized.EnumerateRunes().Take(MaximumScalarCount))
        {
            truncated.Append(rune.ToString());
        }

        return truncated.ToString();
    }
}
