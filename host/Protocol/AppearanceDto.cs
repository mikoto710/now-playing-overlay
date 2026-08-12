using System.Text.Json.Serialization;
using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Protocol;

internal sealed record AppearanceDto
{
    public const int CurrentAppearanceVersion = 1;

    [JsonPropertyName("appearanceVersion")]
    public int AppearanceVersion { get; init; } = CurrentAppearanceVersion;

    [JsonPropertyName("preset")]
    public required string Preset { get; init; }

    [JsonPropertyName("artistColor")]
    public required string ArtistColor { get; init; }

    [JsonPropertyName("trackColor")]
    public required string TrackColor { get; init; }

    [JsonPropertyName("backgroundColor")]
    public required string BackgroundColor { get; init; }

    [JsonPropertyName("backgroundOpacityPercent")]
    public required int BackgroundOpacityPercent { get; init; }

    [JsonPropertyName("cornerRadius")]
    public required int CornerRadius { get; init; }
}

internal static class AppearanceDtoMapper
{
    public static AppearanceDto Map(EffectiveAppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        return new AppearanceDto
        {
            Preset = appearance.Preset switch
            {
                AppearancePreset.Default => "default",
                AppearancePreset.Custom => "custom",
                _ => throw new ArgumentOutOfRangeException(nameof(appearance)),
            },
            ArtistColor = appearance.ArtistColor,
            TrackColor = appearance.TrackColor,
            BackgroundColor = appearance.BackgroundColor,
            BackgroundOpacityPercent = appearance.BackgroundOpacityPercent,
            CornerRadius = appearance.CornerRadius,
        };
    }
}
