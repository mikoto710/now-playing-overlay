import { describe, expect, it } from "vitest";
import { parseNowPlayingState } from "../src/protocol";

const validState = {
  protocolVersion: 1,
  serverInstanceId: "f64b0c0f-73f3-4c0c-8b76-e84b89b77db2",
  snapshotRevision: 42,
  source: "spotify",
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
  artwork: {
    artworkRevision: 7,
    artworkId: "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a",
    url: "/api/v1/artwork/9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a",
  },
  observedAt: "2026-08-09T12:00:00Z",
};

describe("parseNowPlayingState", () => {
  it("accepts the frozen version 1 contract", () => {
    expect(parseNowPlayingState(validState)).toEqual(validState);
  });

  it("accepts the unavailable state with stable null and array shapes", () => {
    expect(
      parseNowPlayingState({
        ...validState,
        snapshotRevision: 0,
        source: null,
        playback: "unavailable",
        track: null,
        artwork: null,
      }),
    ).toMatchObject({ playback: "unavailable", track: null, artwork: null });
  });

  it.each([
    { ...validState, protocolVersion: 2 },
    { ...validState, snapshotRevision: -1 },
    { ...validState, playback: "buffering" },
    { ...validState, track: { ...validState.track, genres: null } },
    { ...validState, artwork: { ...validState.artwork, url: "/wrong" } },
    { ...validState, playback: "playing", track: null, artwork: null },
    { ...validState, source: null },
    { ...validState, playback: "unavailable" },
  ])("rejects unsupported or malformed payloads", (payload) => {
    expect(() => parseNowPlayingState(payload)).toThrow();
  });
});
