using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.Configuration;

internal sealed record OutputSettings
{
    public const int MaximumTextOutputs = 8;

    public TextOutputSettings[] Text { get; init; } = [];

    public JsonOutputSettings Json { get; init; } = new();

    public ArtworkOutputSettings Artwork { get; init; } = new();

    public HistoryOutputSettings History { get; init; } = new();

    public void Validate()
    {
        if (Text is null)
        {
            throw new InvalidDataException("The configured text outputs must not be null.");
        }

        if (Text.Length > MaximumTextOutputs)
        {
            throw new InvalidDataException(
                $"At most {MaximumTextOutputs} text outputs can be configured.");
        }

        if (Text.Any(output => output is null))
        {
            throw new InvalidDataException("A configured text output must not be null.");
        }

        foreach (var output in Text)
        {
            output.Validate();
        }

        (Json ?? throw new InvalidDataException(
            "The configured JSON output must not be null.")).Validate();
        (Artwork ?? throw new InvalidDataException(
            "The configured artwork output must not be null.")).Validate();
        (History ?? throw new InvalidDataException(
            "The configured history output must not be null.")).Validate();

        var enabledPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GetEnabledPaths())
        {
            if (!enabledPaths.Add(Path.GetFullPath(path)))
            {
                throw new InvalidDataException(
                    "Each enabled output must use a different target file.");
            }
        }

        var enabledNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in Text.Where(output => output.Enabled))
        {
            if (!enabledNames.Add(output.Name.Trim()))
            {
                throw new InvalidDataException(
                    "Each enabled text output must use a different name.");
            }
        }
    }

    private IEnumerable<string> GetEnabledPaths()
    {
        foreach (var output in Text.Where(output => output.Enabled))
        {
            yield return output.FilePath!;
        }

        if (Json.Enabled)
        {
            yield return Json.FilePath!;
        }

        if (Artwork.Enabled)
        {
            yield return Artwork.FilePath!;
        }

        if (History.Enabled)
        {
            yield return History.FilePath!;
        }
    }
}

internal sealed record TextOutputSettings
{
    public const int MaximumNameLength = 64;

    public bool Enabled { get; init; }

    public string Name { get; init; } = "Now Playing";

    public string? FilePath { get; init; }

    public string Template { get; init; } = "{nowPlaying}";

    public NoMediaOutputBehavior NoMediaBehavior { get; init; } =
        NoMediaOutputBehavior.Clear;

    public string NoMediaTemplate { get; init; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > MaximumNameLength)
        {
            throw new InvalidDataException(
                $"A text output name must contain 1 to {MaximumNameLength} characters.");
        }

        ValidateTemplate(Template, "text output");
        if (!Enum.IsDefined(NoMediaBehavior))
        {
            throw new InvalidDataException("The text output no-media behavior is invalid.");
        }

        if (NoMediaBehavior == NoMediaOutputBehavior.Placeholder)
        {
            ValidateTemplate(NoMediaTemplate, "no-media placeholder");
        }

        if (Enabled || !string.IsNullOrWhiteSpace(FilePath))
        {
            OutputPathValidator.Validate(FilePath, ".txt", "text output");
        }
    }

    private static void ValidateTemplate(string? template, string description)
    {
        if (template is null)
        {
            throw new InvalidDataException($"The {description} template must not be null.");
        }

        try
        {
            _ = OutputTemplate.Parse(template, allowLineBreaks: true);
        }
        catch (FormatException error)
        {
            throw new InvalidDataException(
                $"The {description} template is invalid. {error.Message}",
                error);
        }
    }
}

internal sealed record JsonOutputSettings
{
    public bool Enabled { get; init; }

    public string? FilePath { get; init; }

    public JsonOutputFormat Format { get; init; } = JsonOutputFormat.Compact;

    public void Validate()
    {
        if (!Enum.IsDefined(Format))
        {
            throw new InvalidDataException("The JSON output format is invalid.");
        }

        if (Enabled || !string.IsNullOrWhiteSpace(FilePath))
        {
            OutputPathValidator.Validate(FilePath, ".json", "JSON output");
        }
    }
}

internal sealed record ArtworkOutputSettings
{
    public bool Enabled { get; init; }

    public string? FilePath { get; init; }

    public MissingArtworkBehavior MissingArtworkBehavior { get; init; } =
        MissingArtworkBehavior.Delete;

    public void Validate()
    {
        if (!Enum.IsDefined(MissingArtworkBehavior))
        {
            throw new InvalidDataException("The missing-artwork behavior is invalid.");
        }

        if (Enabled || !string.IsNullOrWhiteSpace(FilePath))
        {
            OutputPathValidator.Validate(FilePath, ".png", "artwork output");
        }
    }
}

internal sealed record HistoryOutputSettings
{
    public bool Enabled { get; init; }

    public string? FilePath { get; init; }

    public string Template { get; init; } = "{observedAt} {nowPlaying}";

    public void Validate()
    {
        if (Template is null)
        {
            throw new InvalidDataException("The history template must not be null.");
        }

        try
        {
            _ = OutputTemplate.Parse(Template, allowLineBreaks: false);
        }
        catch (FormatException error)
        {
            throw new InvalidDataException(
                $"The history template is invalid. {error.Message}",
                error);
        }

        if (Enabled || !string.IsNullOrWhiteSpace(FilePath))
        {
            OutputPathValidator.Validate(FilePath, ".txt", "history output");
        }
    }
}

internal enum NoMediaOutputBehavior
{
    Clear,
    Placeholder,
    KeepLast,
}

internal enum JsonOutputFormat
{
    Compact,
    Indented,
}

internal enum MissingArtworkBehavior
{
    Delete,
    KeepLast,
}

internal static class OutputPathValidator
{
    public static void Validate(string? filePath, string requiredExtension, string description)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathFullyQualified(filePath))
        {
            throw new InvalidDataException(
                $"The enabled {description} requires an absolute file path.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception error) when (error is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw new InvalidDataException(
                $"The enabled {description} path is invalid.",
                error);
        }

        if (string.IsNullOrEmpty(Path.GetFileName(fullPath)))
        {
            throw new InvalidDataException(
                $"The enabled {description} path must name a file.");
        }

        if (!string.Equals(
                Path.GetExtension(fullPath),
                requiredExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The enabled {description} path must end with '{requiredExtension}'.");
        }

        if (Directory.Exists(fullPath))
        {
            throw new InvalidDataException(
                $"The enabled {description} path points to a directory.");
        }
    }
}
