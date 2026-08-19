import { describe, expect, it } from "vitest";
import { parseNowPlayingState, sourceProviderMaximumLength } from "../src/protocol";

const artworkId = "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a";
const validTimeline = {
  positionMs: 100_000,
  durationMs: 240_000,
  sampledAt: "2026-08-19T03:00:00.000Z",
};
const validState = {
  protocolVersion: 3,
  serverInstanceId: "f64b0c0f-73f3-4c0c-8b76-e84b89b77db2",
  snapshotRevision: 42,
  source: { provider: "windows-media" },
  playback: "playing",
  track: {
    title: "Track title",
    artist: "Artist name",
    albumTitle: "Album name",
    albumArtist: null,
    subtitle: null,
    trackNumber: 3,
    albumTrackCount: 12,
    playbackType: "music",
    genres: ["Rock", "Pop"],
  },
  timeline: validTimeline,
  artwork: {
    artworkRevision: 7,
    artworkId,
    url: `/api/v3/artwork/${artworkId}`,
  },
  observedAt: "2026-08-19T03:00:00.100Z",
};

describe("parseNowPlayingState", () => {
  it("accepts the version 3 contract with a playback timeline", () => {
    expect(parseNowPlayingState(validState)).toEqual(validState);
  });

  it.each(["windows-media", "spotify-api", "future-player", "a".repeat(64)])(
    "accepts bounded canonical provider token %s",
    (provider) => {
      expect(
        parseNowPlayingState({
          ...validState,
          source: { provider },
        }),
      ).toMatchObject({ source: { provider } });
    },
  );

  it.each([
    "",
    "Windows-media",
    "windows_media",
    "-windows-media",
    "windows-media-",
    "windows--media",
    "2player",
    "windows\nmedia",
    "a".repeat(sourceProviderMaximumLength + 1),
  ])("rejects non-canonical provider token %j", (provider) => {
    expect(() =>
      parseNowPlayingState({
        ...validState,
        source: { provider },
      }),
    ).toThrow();
  });

  it.each([
    {
      playback: "playing",
      source: validState.source,
      track: validState.track,
      timeline: validTimeline,
      artwork: null,
    },
    {
      playback: "playing",
      source: validState.source,
      track: validState.track,
      timeline: null,
      artwork: null,
    },
    {
      playback: "paused",
      source: validState.source,
      track: validState.track,
      timeline: validTimeline,
      artwork: null,
    },
    {
      playback: "paused",
      source: validState.source,
      track: null,
      timeline: null,
      artwork: null,
    },
    {
      playback: "stopped",
      source: validState.source,
      track: null,
      timeline: null,
      artwork: null,
    },
    {
      playback: "idle",
      source: validState.source,
      track: null,
      timeline: null,
      artwork: null,
    },
    {
      playback: "unavailable",
      source: null,
      track: null,
      timeline: null,
      artwork: null,
    },
    {
      playback: "unavailable",
      source: validState.source,
      track: null,
      timeline: null,
      artwork: null,
    },
  ])("accepts every legal state-matrix row", (row) => {
    expect(parseNowPlayingState({ ...validState, ...row })).toMatchObject(row);
  });

  it.each([
    { ...validTimeline, positionMs: -1 },
    { ...validTimeline, positionMs: 240_001 },
    { ...validTimeline, positionMs: 1.5 },
    { ...validTimeline, positionMs: Number.MAX_SAFE_INTEGER + 1 },
    { ...validTimeline, durationMs: 0 },
    { ...validTimeline, durationMs: 1.5 },
    { ...validTimeline, sampledAt: "2026-08-19T04:00:00+01:00" },
    { ...validTimeline, sampledAt: "2026-08-19T03:00:00" },
    { ...validTimeline, sampledAt: "not-a-time" },
    { ...validTimeline, extra: true },
    { positionMs: 100_000, durationMs: 240_000 },
  ])("rejects malformed timeline %j", (timeline) => {
    expect(() => parseNowPlayingState({ ...validState, timeline })).toThrow();
  });

  it.each([
    { ...validState, protocolVersion: 2 },
    { ...validState, snapshotRevision: -1 },
    { ...validState, playback: "buffering" },
    { ...validState, timeline: undefined },
    { ...validState, track: { ...validState.track, genres: null } },
    { ...validState, artwork: { ...validState.artwork, url: `/api/v2/artwork/${artworkId}` } },
    { ...validState, playback: "playing", track: null, timeline: null, artwork: null },
    { ...validState, source: null },
    { ...validState, source: { provider: "windows-media", aumid: "private" } },
    { ...validState, playback: "stopped", timeline: validTimeline },
    {
      ...validState,
      playback: "idle",
      track: null,
      timeline: validTimeline,
      artwork: null,
    },
    {
      ...validState,
      playback: "unavailable",
      track: null,
      timeline: validTimeline,
      artwork: null,
    },
    {
      ...validState,
      playback: "idle",
      track: null,
      timeline: null,
      artwork: null,
      source: null,
    },
    { ...validState, playback: "idle", timeline: null, artwork: null },
    { ...validState, playback: "paused", track: null, timeline: null },
  ])("rejects unsupported or malformed payloads", (payload) => {
    expect(() => parseNowPlayingState(payload)).toThrow();
  });
});
