import type { NowPlayingStateDto } from "../protocol";

export interface WidgetViewState {
  visible: boolean;
  artist: string;
  track: string;
  artworkUrl: string | null;
  textRevision: number;
  artworkRevision: number;
}

export interface NowPlayingClientState {
  serverInstanceId: string | null;
  snapshotRevision: number | null;
  snapshot: NowPlayingStateDto | null;
  connectionStale: boolean;
  view: WidgetViewState;
}

export function createInitialState(): NowPlayingClientState {
  return {
    serverInstanceId: null,
    snapshotRevision: null,
    snapshot: null,
    connectionStale: false,
    view: {
      visible: false,
      artist: "",
      track: "",
      artworkUrl: null,
      textRevision: 0,
      artworkRevision: 0,
    },
  };
}

export function reduceSnapshot(
  state: NowPlayingClientState,
  snapshot: NowPlayingStateDto,
): NowPlayingClientState {
  const sameInstance = state.serverInstanceId === snapshot.serverInstanceId;
  if (sameInstance && state.snapshotRevision !== null) {
    if (snapshot.snapshotRevision < state.snapshotRevision) {
      return state;
    }

    if (snapshot.snapshotRevision === state.snapshotRevision) {
      // A reconnect sends the current full snapshot again. It may clear stale hiding, but it
      // must not become a second content update or replay text/artwork animations.
      return state.connectionStale ? recoverFromStaleState(state) : state;
    }
  }

  const nextArtist = snapshot.track?.artist ?? state.view.artist;
  const nextTrack = snapshot.track?.title ?? state.view.track;
  const textChanged =
    snapshot.track !== null && (nextArtist !== state.view.artist || nextTrack !== state.view.track);
  const nextArtworkUrl = snapshot.artwork?.url ?? null;
  const artworkChanged = textChanged || nextArtworkUrl !== state.view.artworkUrl;

  return {
    serverInstanceId: snapshot.serverInstanceId,
    snapshotRevision: snapshot.snapshotRevision,
    snapshot,
    connectionStale: false,
    view: {
      visible: shouldShow(snapshot),
      artist: nextArtist,
      track: nextTrack,
      artworkUrl: nextArtworkUrl,
      textRevision: state.view.textRevision + (textChanged ? 1 : 0),
      // A track switch invalidates the protocol view; the loader owns the brief visual grace.
      artworkRevision: state.view.artworkRevision + (artworkChanged ? 1 : 0),
    },
  };
}

export function markConnectionStale(state: NowPlayingClientState): NowPlayingClientState {
  if (state.connectionStale) {
    return state;
  }

  return {
    ...state,
    connectionStale: true,
    view: state.view.visible ? { ...state.view, visible: false } : state.view,
  };
}

function recoverFromStaleState(state: NowPlayingClientState): NowPlayingClientState {
  const visible = state.snapshot !== null && shouldShow(state.snapshot);
  return {
    ...state,
    connectionStale: false,
    view: visible === state.view.visible ? state.view : { ...state.view, visible },
  };
}

function shouldShow(snapshot: NowPlayingStateDto): boolean {
  return snapshot.playback === "playing" && snapshot.track !== null;
}
