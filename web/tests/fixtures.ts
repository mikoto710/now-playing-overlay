import type { NowPlayingStateDto } from "../src/protocol";

export const instanceA = "f64b0c0f-73f3-4c0c-8b76-e84b89b77db2";
export const instanceB = "82a7e076-9923-4a51-9886-33a8de782f19";
export const artworkIdA = "a".repeat(64);
export const artworkIdB = "b".repeat(64);

export function playingState(overrides: Partial<NowPlayingStateDto> = {}): NowPlayingStateDto {
  return {
    protocolVersion: 3,
    serverInstanceId: instanceA,
    snapshotRevision: 1,
    source: { provider: "windows-media" },
    playback: "playing",
    track: {
      title: "Track A",
      artist: "Artist A",
      albumTitle: "Album",
      albumArtist: null,
      subtitle: null,
      trackNumber: 1,
      albumTrackCount: 10,
      playbackType: "music",
      genres: [],
    },
    timeline: null,
    artwork: {
      artworkRevision: 1,
      artworkId: artworkIdA,
      url: `/api/v3/artwork/${artworkIdA}`,
    },
    observedAt: "2026-08-10T12:00:00Z",
    ...overrides,
  };
}
