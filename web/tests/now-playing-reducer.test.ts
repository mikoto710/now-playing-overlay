import { describe, expect, it } from "vitest";
import {
  createInitialState,
  markConnectionStale,
  reduceSnapshot,
} from "../src/state/now-playing-reducer";
import { artworkIdA, artworkIdB, instanceB, playingState } from "./fixtures";

describe("now-playing reducer", () => {
  it("ignores duplicate and backwards revisions within one host instance", () => {
    const current = reduceSnapshot(createInitialState(), playingState({ snapshotRevision: 5 }));

    expect(reduceSnapshot(current, playingState({ snapshotRevision: 5 }))).toBe(current);
    expect(reduceSnapshot(current, playingState({ snapshotRevision: 4 }))).toBe(current);
  });

  it("accepts a lower revision from a new host without replaying unchanged visuals", () => {
    const current = reduceSnapshot(createInitialState(), playingState({ snapshotRevision: 20 }));
    const restarted = reduceSnapshot(
      current,
      playingState({ serverInstanceId: instanceB, snapshotRevision: 1 }),
    );

    expect(restarted.serverInstanceId).toBe(instanceB);
    expect(restarted.snapshotRevision).toBe(1);
    expect(restarted.view.textRevision).toBe(current.view.textRevision);
    expect(restarted.view.artworkRevision).toBe(current.view.artworkRevision);
  });

  it("changes only visibility when playback pauses and resumes", () => {
    const playing = reduceSnapshot(createInitialState(), playingState());
    const paused = reduceSnapshot(
      playing,
      playingState({ snapshotRevision: 2, playback: "paused" }),
    );
    const resumed = reduceSnapshot(
      paused,
      playingState({ snapshotRevision: 3, playback: "playing" }),
    );

    expect(paused.view.visible).toBe(false);
    expect(resumed.view.visible).toBe(true);
    expect(paused.view.textRevision).toBe(playing.view.textRevision);
    expect(resumed.view.textRevision).toBe(playing.view.textRevision);
    expect(resumed.view.artworkRevision).toBe(playing.view.artworkRevision);
  });

  it("treats late artwork as an artwork-only update", () => {
    const withoutArtwork = reduceSnapshot(createInitialState(), playingState({ artwork: null }));
    const withArtwork = reduceSnapshot(withoutArtwork, playingState({ snapshotRevision: 2 }));

    expect(withArtwork.view.textRevision).toBe(withoutArtwork.view.textRevision);
    expect(withArtwork.view.artworkRevision).toBe(withoutArtwork.view.artworkRevision + 1);
    expect(withArtwork.view.artworkUrl).toBe(`/api/v1/artwork/${artworkIdA}`);
  });

  it("invalidates the old cover when a new track has no artwork", () => {
    const first = reduceSnapshot(createInitialState(), playingState());
    const second = reduceSnapshot(
      first,
      playingState({
        snapshotRevision: 2,
        track: { ...playingState().track!, title: "Track B" },
        artwork: null,
      }),
    );

    expect(second.view.track).toBe("Track B");
    expect(second.view.artworkUrl).toBeNull();
    expect(second.view.textRevision).toBe(first.view.textRevision + 1);
    expect(second.view.artworkRevision).toBe(first.view.artworkRevision + 1);
  });

  it("hides stale state and restores an equal reconnect snapshot without animations", () => {
    const current = reduceSnapshot(createInitialState(), playingState());
    const stale = markConnectionStale(current);
    const recovered = reduceSnapshot(stale, playingState());

    expect(stale.view.visible).toBe(false);
    expect(recovered.view.visible).toBe(true);
    expect(recovered.connectionStale).toBe(false);
    expect(recovered.view.textRevision).toBe(current.view.textRevision);
    expect(recovered.view.artworkRevision).toBe(current.view.artworkRevision);
  });

  it("keeps the latest track and rejects a late snapshot with old artwork", () => {
    const first = reduceSnapshot(createInitialState(), playingState({ artwork: null }));
    const second = reduceSnapshot(
      first,
      playingState({
        snapshotRevision: 2,
        track: { ...playingState().track!, title: "Track B" },
        artwork: null,
      }),
    );
    const latest = reduceSnapshot(
      second,
      playingState({
        snapshotRevision: 3,
        track: { ...playingState().track!, title: "Track C" },
        artwork: null,
      }),
    );
    const lateArtwork = reduceSnapshot(
      latest,
      playingState({
        snapshotRevision: 2,
        track: { ...playingState().track!, title: "Track B" },
        artwork: {
          artworkRevision: 2,
          artworkId: artworkIdB,
          url: `/api/v1/artwork/${artworkIdB}`,
        },
      }),
    );

    expect(lateArtwork).toBe(latest);
    expect(lateArtwork.view.track).toBe("Track C");
    expect(lateArtwork.view.artworkUrl).toBeNull();
  });
});
