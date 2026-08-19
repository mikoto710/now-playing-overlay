import { describe, expect, it } from "vitest";
import { projectTimelinePositionMs } from "../src/timeline";

const sampledAt = "2026-08-19T03:00:00.000Z";
const sampledAtEpochMs = Date.parse(sampledAt);
const timeline = {
  positionMs: 10_000,
  durationMs: 240_000,
  sampledAt,
};

describe("projectTimelinePositionMs", () => {
  it("extrapolates a playing anchor from its sample time", () => {
    expect(projectTimelinePositionMs(timeline, "playing", sampledAtEpochMs + 2_500)).toBe(12_500);
  });

  it("keeps a paused anchor fixed", () => {
    expect(projectTimelinePositionMs(timeline, "paused", sampledAtEpochMs + 60_000)).toBe(10_000);
  });

  it("clamps projections to the media bounds", () => {
    expect(projectTimelinePositionMs(timeline, "playing", sampledAtEpochMs - 20_000)).toBe(0);
    expect(projectTimelinePositionMs(timeline, "playing", sampledAtEpochMs + 300_000)).toBe(
      240_000,
    );
  });
});
