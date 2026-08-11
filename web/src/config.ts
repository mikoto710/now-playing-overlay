export const overlaySize = {
  width: 350,
  height: 70,
} as const;

export const config = {
  stateUrl: "/api/v1/state",
  eventsUrl: "/api/v1/events",
  connectionStaleAfterMs: 5_000,
  widgetAnimationMs: 500,
  textExitAnimationMs: 300,
  textEnterAnimationMs: 500,
  textEnterDelayMs: 100,
  artworkGraceMs: 150,
  artworkFadeMs: 300,
  animationEasing: "ease-in-out",
  marqueeDurationMs: 20_000,
  marqueeGapPx: 40,
  marqueeStartPx: 290,
  marqueeThresholdOffsetPx: 20,
} as const;
