using System.Text.Json;
using System.Text.Json.Serialization;
using NowPlayingOverlay.Host.Media.Sources;
using NowPlayingOverlay.Host.Outputs;

namespace NowPlayingOverlay.Host.Configuration;

internal sealed class ApplicationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<SourceProvider>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
            new JsonStringEnumConverter<AppearancePreset>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
            new JsonStringEnumConverter<ArtworkPosition>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
            new JsonStringEnumConverter<ArtworkFit>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
            new JsonStringEnumConverter<NoMediaOutputBehavior>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
            new JsonStringEnumConverter<JsonOutputFormat>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
            new JsonStringEnumConverter<MissingArtworkBehavior>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
            new JsonStringEnumConverter<WindowTitleParseMode>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
            new JsonStringEnumConverter<WindowTitleSplitOccurrence>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
            new JsonStringEnumConverter<WindowTitleField>(
                JsonNamingPolicy.KebabCaseLower,
                allowIntegerValues: false),
        },
    };
    private readonly object _gate = new();
    private readonly string? _defaultOutputDirectory;
    private readonly string _filePath;

    public ApplicationSettingsStore(string filePath, string? defaultOutputDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _defaultOutputDirectory = defaultOutputDirectory is null
            ? null
            : Path.GetFullPath(defaultOutputDirectory);
    }

    public ApplicationSettingsLoadResult Load()
    {
        lock (_gate)
        {
            return LoadCore();
        }
    }

    public void Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            SaveCore(settings);
        }
    }

    public ApplicationSettings Update(Func<ApplicationSettings, ApplicationSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            var loaded = LoadCore();
            var settings = update(loaded.Settings)
                ?? throw new InvalidOperationException("The settings update returned null.");
            SaveCore(settings);
            return settings;
        }
    }

    private ApplicationSettingsLoadResult LoadCore()
    {
        if (!File.Exists(_filePath))
        {
            return new ApplicationSettingsLoadResult(CreateDefaultSettings(), Warning: null);
        }

        try
        {
            var document = JsonSerializer.Deserialize<ApplicationSettingsDocument>(
                File.ReadAllText(_filePath),
                JsonOptions) ?? throw new InvalidDataException("The settings file is empty.");
            var source = ReadSource(document.Source, out var migratedWindowsMediaInstanceId);
            var windowsMedia = ReadWindowsMedia(
                document.WindowsMedia,
                migratedWindowsMediaInstanceId);
            var appearance = ReadAppearance(document.Appearance, out var appearanceWarning);
            var windowTitle = ReadWindowTitle(document.WindowTitle, out var windowTitleWarning);
            var outputs = ApplyDefaultOutputPaths(
                ReadOutputs(document.Outputs, out var outputsWarning));
            var settings = new ApplicationSettings
            {
                Port = document.Port,
                Source = source,
                WindowsMedia = windowsMedia,
                Spotify = document.Spotify
                    ?? throw new InvalidDataException("The configured Spotify connection must not be null."),
                WindowTitle = windowTitle,
                Appearance = appearance,
                Outputs = outputs,
            };
            settings.Validate();
            return new ApplicationSettingsLoadResult(
                settings,
                CombineWarnings(appearanceWarning, windowTitleWarning, outputsWarning));
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException)
        {
            return new ApplicationSettingsLoadResult(
                CreateDefaultSettings(),
                $"Could not read '{_filePath}'; the default settings will be used. {error.Message}");
        }
    }

    private ApplicationSettings CreateDefaultSettings()
    {
        return new ApplicationSettings
        {
            Outputs = ApplyDefaultOutputPaths(new OutputSettings()),
        };
    }

    private OutputSettings ApplyDefaultOutputPaths(OutputSettings outputs)
    {
        return _defaultOutputDirectory is null
            ? outputs
            : outputs.WithDefaultFilePaths(_defaultOutputDirectory);
    }

    private SourceSelectionSettings ReadSource(
        JsonElement element,
        out string? migratedWindowsMediaInstanceId)
    {
        migratedWindowsMediaInstanceId = null;
        if (element.ValueKind is JsonValueKind.Undefined)
        {
            return SourceSelectionSettings.WindowsMedia(instanceId: null);
        }

        if (element.ValueKind is JsonValueKind.Null)
        {
            throw new InvalidDataException("The configured source must not be null.");
        }

        var document = element.Deserialize<SourceSelectionSettingsDocument>(JsonOptions)
            ?? throw new InvalidDataException("The configured source is empty.");
        var hasInstanceId = element.TryGetProperty("instanceId", out _);
        var hasLegacyInstanceId = element.TryGetProperty("sourceAppUserModelId", out _);
        if (hasInstanceId && hasLegacyInstanceId)
        {
            throw new InvalidDataException(
                "The configured source must not contain both instanceId and sourceAppUserModelId.");
        }

        if (hasLegacyInstanceId)
        {
            migratedWindowsMediaInstanceId = document.SourceAppUserModelId;
            return document.Provider switch
            {
                SourceProvider.WindowsMedia =>
                    SourceSelectionSettings.WindowsMedia(document.SourceAppUserModelId),
                SourceProvider.SpotifyApi => SourceSelectionSettings.SpotifyApi(),
                _ => throw new InvalidDataException("The configured source provider is not supported."),
            };
        }

        var source = new SourceSelectionSettings
        {
            Provider = document.Provider,
            InstanceId = document.InstanceId,
        };
        source.Validate();
        if (source.Provider == SourceProvider.WindowsMedia)
        {
            migratedWindowsMediaInstanceId = source.InstanceId;
        }

        return source;
    }

    private WindowsMediaSettings ReadWindowsMedia(
        JsonElement element,
        string? migratedWindowsMediaInstanceId)
    {
        if (element.ValueKind is JsonValueKind.Undefined)
        {
            return new WindowsMediaSettings { LastInstanceId = migratedWindowsMediaInstanceId };
        }

        if (element.ValueKind is JsonValueKind.Null)
        {
            throw new InvalidDataException("The configured Windows Media settings must not be null.");
        }

        var settings = element.Deserialize<WindowsMediaSettings>(JsonOptions)
            ?? throw new InvalidDataException("The configured Windows Media settings are empty.");
        settings.Validate();
        return settings;
    }

    private AppearanceSettings ReadAppearance(
        JsonElement? element,
        out string? warning)
    {
        warning = null;
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new AppearanceSettings();
        }

        try
        {
            var appearance = element.Value.Deserialize<AppearanceSettings>(JsonOptions)
                ?? throw new InvalidDataException("The configured appearance is empty.");
            appearance.Validate();
            return appearance;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            warning =
                $"Could not read the appearance in '{_filePath}'; the default appearance will be used. {error.Message}";
            return new AppearanceSettings();
        }
    }

    private WindowTitleSettings ReadWindowTitle(JsonElement? element, out string? warning)
    {
        warning = null;
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new WindowTitleSettings();
        }

        try
        {
            var settings = element.Value.Deserialize<WindowTitleSettings>(JsonOptions)
                ?? throw new InvalidDataException("The configured Window Title settings are empty.");
            settings.Validate();
            return settings;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            warning =
                $"Could not read the Window Title settings in '{_filePath}'; Window Title will remain unconfigured. {error.Message}";
            return new WindowTitleSettings();
        }
    }

    private OutputSettings ReadOutputs(
        JsonElement? element,
        out string? warning)
    {
        warning = null;
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new OutputSettings();
        }

        try
        {
            if (element.Value.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.Array)
            {
                return ReadLegacyOutputs(element.Value, out warning);
            }

            var outputs = element.Value.Deserialize<OutputSettings>(JsonOptions)
                ?? throw new InvalidDataException("The configured outputs are empty.");
            outputs.Validate();
            return outputs;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            warning =
                $"Could not read the outputs in '{_filePath}'; outputs will remain disabled. {error.Message}";
            return new OutputSettings();
        }
    }

    private OutputSettings ReadLegacyOutputs(JsonElement element, out string? warning)
    {
        var legacy = element.Deserialize<LegacyOutputSettingsDocument>(JsonOptions)
            ?? throw new InvalidDataException("The configured outputs are empty.");
        var textOutputs = legacy.Text
            ?? throw new InvalidDataException("The configured text outputs must not be null.");
        var outputs = new OutputSettings
        {
            Text = textOutputs.Length == 0
                ? new TextOutputSettings()
                : textOutputs[0].ToSettings(),
            Json = legacy.Json
                ?? throw new InvalidDataException("The configured JSON output must not be null."),
            Artwork = legacy.Artwork
                ?? throw new InvalidDataException("The configured artwork output must not be null."),
            History = legacy.History
                ?? throw new InvalidDataException("The configured history output must not be null."),
        };
        outputs.Validate();
        warning = textOutputs.Length > 1
            ? "Multiple prototype TXT outputs were found; only the first was retained after switching to the simpler single-output settings."
            : null;
        return outputs;
    }

    private static string? CombineWarnings(params string?[] warnings)
    {
        var present = warnings.Where(warning => !string.IsNullOrEmpty(warning)).ToArray();
        return present.Length == 0 ? null : string.Join(" ", present);
    }

    private void SaveCore(ApplicationSettings settings)
    {
        settings.Validate();
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The primary write/move result is more useful than a temporary-file cleanup failure.
            }
        }
    }

    private sealed record ApplicationSettingsDocument
    {
        public int Port { get; init; } = HostOptions.DefaultPort;

        public JsonElement Source { get; init; }

        public JsonElement WindowsMedia { get; init; }

        public SpotifyConnectionSettings? Spotify { get; init; } = new();

        public JsonElement? WindowTitle { get; init; }

        public JsonElement? Appearance { get; init; }

        public JsonElement? Outputs { get; init; }
    }

    private sealed record SourceSelectionSettingsDocument
    {
        public SourceProvider Provider { get; init; } = SourceProvider.WindowsMedia;

        public string? InstanceId { get; init; }

        public string? SourceAppUserModelId { get; init; }
    }

    private sealed record LegacyOutputSettingsDocument
    {
        public LegacyTextOutputSettingsDocument[]? Text { get; init; } = [];

        public JsonOutputSettings? Json { get; init; } = new();

        public ArtworkOutputSettings? Artwork { get; init; } = new();

        public HistoryOutputSettings? History { get; init; } = new();
    }

    private sealed record LegacyTextOutputSettingsDocument
    {
        public bool Enabled { get; init; }

        public string Name { get; init; } = "Now Playing";

        public string? FilePath { get; init; }

        public string Template { get; init; } = OutputTemplate.DefaultNowPlaying;

        public NoMediaOutputBehavior NoMediaBehavior { get; init; } =
            NoMediaOutputBehavior.Clear;

        public string NoMediaTemplate { get; init; } = string.Empty;

        public TextOutputSettings ToSettings()
        {
            return new TextOutputSettings
            {
                Enabled = Enabled,
                FilePath = FilePath,
                Template = Template,
                NoMediaBehavior = NoMediaBehavior,
                NoMediaTemplate = NoMediaTemplate,
            };
        }
    }
}

internal sealed record ApplicationSettingsLoadResult(
    ApplicationSettings Settings,
    string? Warning);
