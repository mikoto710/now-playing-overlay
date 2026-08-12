import { describe, expect, it } from "vitest";
import { applyAppearance, parseAppearance } from "../src/appearance";

describe("appearance", () => {
  it("parses one representative custom appearance and applies only fixed CSS properties", () => {
    const appearance = parseAppearance({
      appearanceVersion: 1,
      preset: "custom",
      artistColor: "#123456",
      trackColor: "#ABCDEF",
      backgroundColor: "#102030",
      backgroundOpacityPercent: 65,
      cornerRadius: 12,
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
      ]),
    );
  });

  it("rejects an out-of-range appearance as one invalid configuration", () => {
    expect(() =>
      parseAppearance({
        appearanceVersion: 1,
        preset: "custom",
        artistColor: "#123456",
        trackColor: "#ABCDEF",
        backgroundColor: "#102030",
        backgroundOpacityPercent: 65,
        cornerRadius: 36,
      }),
    ).toThrow("Corner radius");
  });
});
