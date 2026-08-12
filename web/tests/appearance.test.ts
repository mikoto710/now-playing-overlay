import { describe, expect, it } from "vitest";
import { applyAppearance, defaultAppearance, parseAppearance } from "../src/appearance";

describe("appearance", () => {
  it("parses one representative custom appearance and applies only fixed CSS properties", () => {
    const appearance = parseAppearance({
      appearanceVersion: 3,
      preset: "custom",
      artistColor: "#123456",
      trackColor: "#ABCDEF",
      backgroundColor: "#102030",
      backgroundOpacityPercent: 65,
      cornerRadius: 12,
      fontFamily: "Segoe UI",
      artistFontSize: 18,
      artistFontWeight: 500,
      trackFontSize: 24,
      trackFontWeight: 600,
      artworkVisible: false,
      artworkSize: 48,
      artworkPosition: "right",
      artworkFit: "cover",
      artworkCornerRadius: 8,
    });
    const properties = new Map<string, string>();

    applyAppearance(
      {
        setProperty: (name, value) => properties.set(name, value),
      },
      appearance,
    );

    expect(properties).toEqual(
      new Map([
        ["--overlay-artist-color", "#123456"],
        ["--overlay-track-color", "#ABCDEF"],
        ["--overlay-background", "rgba(16, 32, 48, 0.65)"],
        ["--overlay-corner-radius", "12px"],
        [
          "--overlay-font-family",
          '"Segoe UI", "SF Pro Display", "Segoe UI", Helvetica, Arial, sans-serif',
        ],
        ["--overlay-artist-font-size", "18px"],
        ["--overlay-artist-font-weight", "500"],
        ["--overlay-artist-line-height", "21px"],
        ["--overlay-track-font-size", "24px"],
        ["--overlay-track-font-weight", "600"],
        ["--overlay-track-line-height", "28px"],
        ["--overlay-artwork-visibility", "hidden"],
        ["--overlay-artwork-size", "48px"],
        ["--overlay-artwork-top", "11px"],
        ["--overlay-artwork-left", "302px"],
        ["--overlay-artwork-fit", "cover"],
        ["--overlay-artwork-corner-radius", "8px"],
        ["--overlay-details-left", "0px"],
        ["--overlay-details-width", "350px"],
      ]),
    );
  });

  it("rejects an out-of-range appearance as one invalid configuration", () => {
    expect(() =>
      parseAppearance({
        appearanceVersion: 3,
        preset: "custom",
        artistColor: "#123456",
        trackColor: "#ABCDEF",
        backgroundColor: "#102030",
        backgroundOpacityPercent: 65,
        cornerRadius: 12,
        fontFamily: null,
        artistFontSize: 19,
        artistFontWeight: 600,
        trackFontSize: 22,
        trackFontWeight: 700,
        artworkVisible: true,
        artworkSize: 70,
        artworkPosition: "left",
        artworkFit: "contain",
        artworkCornerRadius: 0,
      }),
    ).toThrow("Artist font size");
  });

  it("rejects an out-of-range artwork size as one invalid configuration", () => {
    expect(() =>
      parseAppearance({
        ...defaultAppearance,
        artworkSize: 39,
      }),
    ).toThrow("Artwork size");
  });
});
