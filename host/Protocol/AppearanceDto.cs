using System.Text.Json.Serialization;
using NowPlayingOverlay.Host.Configuration;

namespace NowPlayingOverlay.Host.Protocol;

internal sealed record AppearanceDto
{
    public const int CurrentAppearanceVersion = 2;

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

    [JsonPropertyName("fontFamily")]
    public string? FontFamily { get; init; }

    [JsonPropertyName("artistFontSize")]
    public required int ArtistFontSize { get; init; }

    [JsonPropertyName("artistFontWeight")]
    public required int ArtistFontWeight { get; init; }

    [JsonPropertyName("trackFontSize")]
    public required int TrackFontSize { get; init; }

    [JsonPropertyName("trackFontWeight")]
    public required int TrackFontWeight { get; init; }
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
            FontFamily = appearance.FontFamily,
            ArtistFontSize = appearance.ArtistFontSize,
            ArtistFontWeight = appearance.ArtistFontWeight,
            TrackFontSize = appearance.TrackFontSize,
            TrackFontWeight = appearance.TrackFontWeight,
        };
    }
}
