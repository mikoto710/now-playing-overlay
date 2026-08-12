namespace NowPlayingOverlay.Host.Configuration;

internal enum AppearancePreset
{
    Default,
    Custom,
}

internal sealed record AppearanceSettings
{
    public AppearancePreset Preset { get; init; } = AppearancePreset.Default;

    public CustomAppearanceSettings Custom { get; init; } = new();

    public void Validate()
    {
        if (!Enum.IsDefined(Preset))
        {
            throw new InvalidDataException("The configured appearance preset is invalid.");
        }

        if (Custom is null)
        {
            throw new InvalidDataException("The configured custom appearance must not be null.");
        }

        Custom.Validate();
    }

    public EffectiveAppearanceSettings ToEffective()
    {
        Validate();
        var values = Preset == AppearancePreset.Default
            ? new CustomAppearanceSettings()
            : Custom;
        return new EffectiveAppearanceSettings(
            Preset,
            values.ArtistColor,
            values.TrackColor,
            values.BackgroundColor,
            values.BackgroundOpacityPercent,
            values.CornerRadius);
    }
}

internal sealed record CustomAppearanceSettings
{
    public const string DefaultArtistColor = "#25C7A0";
    public const string DefaultTrackColor = "#FFFFFF";
    public const string DefaultBackgroundColor = "#1B1D20";
    public const int DefaultBackgroundOpacityPercent = 100;
    public const int DefaultCornerRadius = 0;

    public string ArtistColor { get; init; } = DefaultArtistColor;

    public string TrackColor { get; init; } = DefaultTrackColor;

    public string BackgroundColor { get; init; } = DefaultBackgroundColor;

    public int BackgroundOpacityPercent { get; init; } = DefaultBackgroundOpacityPercent;

    public int CornerRadius { get; init; } = DefaultCornerRadius;

    public void Validate()
    {
        ValidateColor(ArtistColor, "artist");
        ValidateColor(TrackColor, "track");
        ValidateColor(BackgroundColor, "background");
        if (BackgroundOpacityPercent is < 0 or > 100)
        {
            throw new InvalidDataException(
                "The configured background opacity must be between 0 and 100 percent.");
        }

        if (CornerRadius is < 0 or > 35)
        {
            throw new InvalidDataException(
                "The configured corner radius must be between 0 and 35 logical pixels.");
        }
    }

    private static void ValidateColor(string value, string name)
    {
        if (!IsCanonicalHexColor(value))
        {
            throw new InvalidDataException(
                $"The configured {name} color must use canonical #RRGGBB format.");
        }
    }

    private static bool IsCanonicalHexColor(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed record EffectiveAppearanceSettings(
    AppearancePreset Preset,
    string ArtistColor,
    string TrackColor,
    string BackgroundColor,
    int BackgroundOpacityPercent,
    int CornerRadius);
