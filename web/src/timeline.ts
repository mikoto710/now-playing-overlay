import type { PlaybackState, PlaybackTimelineDto } from "./protocol";

export function projectTimelinePositionMs(
  timeline: PlaybackTimelineDto | null,
  playback: PlaybackState,
  nowEpochMs: number,
): number | null {
  if (timeline === null || (playback !== "playing" && playback !== "paused")) {
    return null;
  }

  if (!Number.isFinite(nowEpochMs)) {
    throw new RangeError("Timeline projection time must be finite.");
  }

  const sampledAtEpochMs = Date.parse(timeline.sampledAt);
  if (!Number.isFinite(sampledAtEpochMs)) {
    throw new RangeError("Timeline sample time must be valid.");
  }

  const position =
    playback === "playing"
      ? timeline.positionMs + (nowEpochMs - sampledAtEpochMs)
      : timeline.positionMs;
  return Math.min(timeline.durationMs, Math.max(0, position));
}
