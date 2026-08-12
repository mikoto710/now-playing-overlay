using System.Text.Json;
using System.Text.Json.Serialization;
using NowPlayingOverlay.Host.Media;

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
        },
    };
    private readonly object _gate = new();
    private readonly string _filePath;

    public ApplicationSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
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
            return new ApplicationSettingsLoadResult(new ApplicationSettings(), Warning: null);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(
                File.ReadAllText(_filePath),
                JsonOptions) ?? throw new InvalidDataException("The settings file is empty.");
            settings.Validate();
            return new ApplicationSettingsLoadResult(settings, Warning: null);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException)
        {
            return new ApplicationSettingsLoadResult(
                new ApplicationSettings(),
                $"Could not read '{_filePath}'; the default settings will be used. {error.Message}");
        }
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
}

internal sealed record ApplicationSettingsLoadResult(
    ApplicationSettings Settings,
    string? Warning);
