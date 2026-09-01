using System.Globalization;
using System.Text;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Models;

namespace NowPlayingOverlay.Host.Outputs;

/// <summary>
/// Parses and renders the bounded handwritten output-template grammar.
/// </summary>
internal sealed class OutputTemplate
{
    public const int MaximumLength = 4096;
    public const int MaximumTruncateScalars = 4096;
    public const string DefaultNowPlaying = "{nowPlaying}";
    public const string DefaultHistory = "{observedAt} {nowPlaying}";

    private static readonly HashSet<string> KnownTokens = new(StringComparer.Ordinal)
    {
        "nowPlaying",
        "title",
        "artist",
        "albumTitle",
        "albumArtist",
        "subtitle",
        "trackNumber",
        "albumTrackCount",
        "genres",
        "playback",
        "source",
        "position",
        "duration",
        "observedAt",
        "newline",
    };

    private readonly IReadOnlyList<Segment> _segments;

    private OutputTemplate(IReadOnlyList<Segment> segments)
    {
        _segments = segments;
    }

    public static OutputTemplate Parse(string template, bool allowLineBreaks)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Length > MaximumLength)
        {
            throw new FormatException(
                $"Templates cannot exceed {MaximumLength} UTF-16 code units.");
        }

        if (!allowLineBreaks && ContainsLineBreak(template))
        {
            throw new FormatException("History templates must render one line per record.");
        }

        var segments = new List<Segment>();
        var literal = new StringBuilder();
        for (var index = 0; index < template.Length; index++)
        {
            var character = template[index];
            if (character == '{')
            {
                if (index + 1 < template.Length && template[index + 1] == '{')
                {
                    literal.Append('{');
                    index++;
                    continue;
                }

                FlushLiteral(literal, segments);
                var close = template.IndexOf('}', index + 1);
                if (close < 0)
                {
                    throw new FormatException("A template token is missing its closing brace.");
                }

                var expression = template[(index + 1)..close];
                segments.Add(ParseToken(expression, allowLineBreaks));
                index = close;
                continue;
            }

            if (character == '}')
            {
                if (index + 1 < template.Length && template[index + 1] == '}')
                {
                    literal.Append('}');
                    index++;
                    continue;
                }

                throw new FormatException("A literal closing brace must be escaped as '}}'.");
            }

            literal.Append(character);
        }

        FlushLiteral(literal, segments);
        return new OutputTemplate(segments.AsReadOnly());
    }

    public string Render(NowPlayingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var rendered = new StringBuilder();
        foreach (var segment in _segments)
        {
            segment.Append(rendered, snapshot);
        }

        return rendered.ToString();
    }

    private static Segment ParseToken(string expression, bool allowLineBreaks)
    {
        if (expression.Length == 0)
        {
            throw new FormatException("Template tokens must not be empty.");
        }

        var parts = expression.Split('|');
        var token = parts[0];
        if (!KnownTokens.Contains(token))
        {
            throw new FormatException($"Unknown template token '{token}'.");
        }

        if (token == "newline" && !allowLineBreaks)
        {
            throw new FormatException("History templates cannot use the newline token.");
        }

        var modifiers = new List<Modifier>();
        for (var index = 1; index < parts.Length; index++)
        {
            var value = parts[index];
            if (value == "upper")
            {
                modifiers.Add(new Modifier(ModifierKind.Upper, 0));
            }
            else if (value == "lower")
            {
                modifiers.Add(new Modifier(ModifierKind.Lower, 0));
            }
            else if (value.StartsWith("truncate:", StringComparison.Ordinal)
                && int.TryParse(
                    value.AsSpan("truncate:".Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var scalarCount)
                && scalarCount is >= 0 and <= MaximumTruncateScalars)
            {
                modifiers.Add(new Modifier(ModifierKind.Truncate, scalarCount));
            }
            else
            {
                throw new FormatException($"Unknown template modifier '{value}'.");
            }
        }

        if (token == "newline" && modifiers.Count > 0)
        {
            throw new FormatException("The newline token does not accept modifiers.");
        }

        return new TokenSegment(token, modifiers.AsReadOnly());
    }

    private static void FlushLiteral(StringBuilder literal, ICollection<Segment> segments)
    {
        if (literal.Length == 0)
        {
            return;
        }

        segments.Add(new LiteralSegment(literal.ToString()));
        literal.Clear();
    }

    private static bool ContainsLineBreak(string value)
    {
        return value.AsSpan().IndexOfAny('\r', '\n') >= 0
            || value.Contains('\u0085')
            || value.Contains('\u2028')
            || value.Contains('\u2029');
    }

    private static string ResolveToken(string token, NowPlayingSnapshot snapshot)
    {
        var track = snapshot.Track;
        return token switch
        {
            "nowPlaying" => track is null
                ? string.Empty
                : string.IsNullOrEmpty(track.Artist)
                    ? track.Title
                    : $"{track.Artist} - {track.Title}",
            "title" => track?.Title ?? string.Empty,
            "artist" => track?.Artist ?? string.Empty,
            "albumTitle" => track?.AlbumTitle ?? string.Empty,
            "albumArtist" => track?.AlbumArtist ?? string.Empty,
            "subtitle" => track?.Subtitle ?? string.Empty,
            "trackNumber" => FormatNumber(track?.TrackNumber),
            "albumTrackCount" => FormatNumber(track?.AlbumTrackCount),
            "genres" => track is null ? string.Empty : string.Join(", ", track.Genres),
            "playback" => snapshot.Playback.ToString().ToLowerInvariant(),
            "source" => snapshot.Source?.Key.Provider.ToProtocolValue() ?? string.Empty,
            "position" => FormatDuration(snapshot.Timeline?.PositionMs),
            "duration" => FormatDuration(snapshot.Timeline?.DurationMs),
            "observedAt" => snapshot.ObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            "newline" => Environment.NewLine,
            _ => throw new InvalidOperationException("A parsed output token is unsupported."),
        };
    }

    private static string FormatNumber(uint? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatDuration(long? milliseconds)
    {
        if (milliseconds is null)
        {
            return string.Empty;
        }

        var totalSeconds = milliseconds.Value / 1000;
        var seconds = totalSeconds % 60;
        var totalMinutes = totalSeconds / 60;
        if (totalMinutes < 60)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{totalMinutes}:{seconds:00}");
        }

        var minutes = totalMinutes % 60;
        var hours = totalMinutes / 60;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours}:{minutes:00}:{seconds:00}");
    }

    private static string ApplyModifiers(string value, IReadOnlyList<Modifier> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            value = modifier.Kind switch
            {
                ModifierKind.Upper => value.ToUpperInvariant(),
                ModifierKind.Lower => value.ToLowerInvariant(),
                ModifierKind.Truncate => Truncate(value, modifier.Value),
                _ => throw new InvalidOperationException("A parsed output modifier is unsupported."),
            };
        }

        return value;
    }

    private static string Truncate(string value, int scalarCount)
    {
        var result = new StringBuilder(value.Length);
        var written = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (written == scalarCount)
            {
                break;
            }

            result.Append(rune.ToString());
            written++;
        }

        return written == 0 ? string.Empty : result.ToString();
    }

    private abstract class Segment
    {
        public abstract void Append(StringBuilder builder, NowPlayingSnapshot snapshot);
    }

    private sealed class LiteralSegment(string value) : Segment
    {
        public override void Append(StringBuilder builder, NowPlayingSnapshot snapshot)
        {
            builder.Append(value);
        }
    }

    private sealed class TokenSegment(
        string token,
        IReadOnlyList<Modifier> modifiers) : Segment
    {
        public override void Append(StringBuilder builder, NowPlayingSnapshot snapshot)
        {
            builder.Append(ApplyModifiers(ResolveToken(token, snapshot), modifiers));
        }
    }

    private readonly record struct Modifier(ModifierKind Kind, int Value);

    private enum ModifierKind
    {
        Upper,
        Lower,
        Truncate,
    }
}
