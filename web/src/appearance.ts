export type AppearancePreset = "default" | "custom";

export interface Appearance {
  readonly appearanceVersion: 1;
  readonly preset: AppearancePreset;
  readonly artistColor: string;
  readonly trackColor: string;
  readonly backgroundColor: string;
  readonly backgroundOpacityPercent: number;
  readonly cornerRadius: number;
}

export interface StylePropertyTarget {
  setProperty(name: string, value: string): void;
}

export const defaultAppearance: Appearance = {
  appearanceVersion: 1,
  preset: "default",
  artistColor: "#25C7A0",
  trackColor: "#FFFFFF",
  backgroundColor: "#1B1D20",
  backgroundOpacityPercent: 100,
  cornerRadius: 0,
};

const appearanceKeys = new Set([
  "appearanceVersion",
  "preset",
  "artistColor",
  "trackColor",
  "backgroundColor",
  "backgroundOpacityPercent",
  "cornerRadius",
]);
const canonicalColor = /^#[0-9A-F]{6}$/;

export function parseAppearance(value: unknown): Appearance {
  if (!isRecord(value) || !hasExactKeys(value)) {
    throw new Error("Appearance must be an object with the supported fields only.");
  }
  if (value.appearanceVersion !== 1) {
    throw new Error("Appearance version is not supported.");
  }
  if (value.preset !== "default" && value.preset !== "custom") {
    throw new Error("Appearance preset is invalid.");
  }
  for (const field of ["artistColor", "trackColor", "backgroundColor"] as const) {
    if (typeof value[field] !== "string" || !canonicalColor.test(value[field])) {
      throw new Error(`${field} must use canonical #RRGGBB format.`);
    }
  }
  const backgroundOpacityPercent = value.backgroundOpacityPercent;
  if (
    typeof backgroundOpacityPercent !== "number" ||
    !Number.isInteger(backgroundOpacityPercent) ||
    backgroundOpacityPercent < 0 ||
    backgroundOpacityPercent > 100
  ) {
    throw new Error("Background opacity must be an integer from 0 to 100.");
  }
  const cornerRadius = value.cornerRadius;
  if (
    typeof cornerRadius !== "number" ||
    !Number.isInteger(cornerRadius) ||
    cornerRadius < 0 ||
    cornerRadius > 35
  ) {
    throw new Error("Corner radius must be an integer from 0 to 35.");
  }

  return value as unknown as Appearance;
}

export async function loadAppearance(url: string): Promise<Appearance> {
  try {
    const response = await fetch(url, { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Appearance request failed with HTTP ${response.status}.`);
    }
    return parseAppearance(await response.json());
  } catch (error) {
    console.warn("Could not load overlay appearance; using Default.", error);
    return defaultAppearance;
  }
}

export function applyAppearance(target: StylePropertyTarget, appearance: Appearance): void {
  target.setProperty("--overlay-artist-color", appearance.artistColor);
  target.setProperty("--overlay-track-color", appearance.trackColor);
  target.setProperty(
    "--overlay-background",
    toCssBackground(appearance.backgroundColor, appearance.backgroundOpacityPercent),
  );
  target.setProperty("--overlay-corner-radius", `${appearance.cornerRadius}px`);
}

function hasExactKeys(value: Record<string, unknown>): boolean {
  const keys = Object.keys(value);
  return keys.length === appearanceKeys.size && keys.every((key) => appearanceKeys.has(key));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function toCssBackground(color: string, opacityPercent: number): string {
  const red = Number.parseInt(color.slice(1, 3), 16);
  const green = Number.parseInt(color.slice(3, 5), 16);
  const blue = Number.parseInt(color.slice(5, 7), 16);
  if (opacityPercent === 100) {
    return `rgb(${red}, ${green}, ${blue})`;
  }
  return `rgba(${red}, ${green}, ${blue}, ${opacityPercent / 100})`;
}
