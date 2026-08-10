import { afterEach, describe, expect, it, vi } from "vitest";
import type { NowPlayingStateDto } from "../src/protocol";
import {
  OverlayClient,
  type EventSourceLike,
  type OverlayClientCallbacks,
  type OverlayClientDependencies,
} from "../src/services/overlay-client";
import {
  createInitialState,
  markConnectionStale,
  reduceSnapshot,
} from "../src/state/now-playing-reducer";
import { playingState } from "./fixtures";

class FakeEventSource implements EventSourceLike {
  onerror: ((event: Event) => void) | null = null;
  closed = false;
  private stateListener: ((event: { data: string }) => void) | null = null;

  addEventListener(type: "state", listener: (event: { data: string }) => void): void {
    if (type === "state") {
      this.stateListener = listener;
    }
  }

  close(): void {
    this.closed = true;
  }

  emit(snapshot: unknown): void {
    this.stateListener?.({ data: JSON.stringify(snapshot) });
  }

  disconnect(): void {
    this.onerror?.(new Event("error"));
  }
}

afterEach(() => {
  vi.useRealTimers();
});

describe("OverlayClient", () => {
  it("loads the initial full snapshot", async () => {
    const source = new FakeEventSource();
    const snapshots: NowPlayingStateDto[] = [];
    const client = createClient(source, snapshots, async () =>
      jsonResponse(playingState({ snapshotRevision: 3 })),
    );

    client.start();
    await vi.waitFor(() => expect(snapshots).toHaveLength(1));

    expect(snapshots[0]?.snapshotRevision).toBe(3);
  });

  it("lets reducer revision rules resolve the GET and SSE race", async () => {
    const source = new FakeEventSource();
    let resolveFetch!: (response: Response) => void;
    const response = new Promise<Response>((resolve) => {
      resolveFetch = resolve;
    });
    let state = createInitialState();
    const client = createClient(
      source,
      [],
      async () => response,
      (snapshot) => {
        state = reduceSnapshot(state, snapshot);
      },
    );

    client.start();
    source.emit(playingState({ snapshotRevision: 2 }));
    resolveFetch(jsonResponse(playingState({ snapshotRevision: 1 })));
    await flushPromises();

    expect(state.snapshotRevision).toBe(2);
  });

  it("hides after the disconnect grace period and restores an equal reconnect snapshot", () => {
    vi.useFakeTimers();
    const source = new FakeEventSource();
    let state = createInitialState();
    const client = createClient(
      source,
      [],
      () => new Promise<Response>(() => undefined),
      (snapshot) => {
        state = reduceSnapshot(state, snapshot);
      },
      () => {
        state = markConnectionStale(state);
      },
    );

    client.start();
    source.emit(playingState());
    const textRevision = state.view.textRevision;
    source.disconnect();
    vi.advanceTimersByTime(4_999);
    expect(state.view.visible).toBe(true);
    vi.advanceTimersByTime(1);
    expect(state.view.visible).toBe(false);

    source.emit(playingState());
    expect(state.view.visible).toBe(true);
    expect(state.view.textRevision).toBe(textRevision);
  });

  it("fails closed and stops the stream after an invalid protocol event", () => {
    const source = new FakeEventSource();
    const onStale = vi.fn();
    const onProtocolError = vi.fn();
    const client = createClient(
      source,
      [],
      () => new Promise<Response>(() => undefined),
      undefined,
      onStale,
      onProtocolError,
    );

    client.start();
    source.emit({ protocolVersion: 2 });
    source.emit(playingState());

    expect(source.closed).toBe(true);
    expect(onStale).toHaveBeenCalledOnce();
    expect(onProtocolError).toHaveBeenCalledOnce();
  });
});

function createClient(
  source: FakeEventSource,
  snapshots: NowPlayingStateDto[],
  fetchState: () => Promise<Response>,
  onSnapshot: (snapshot: NowPlayingStateDto) => void = (snapshot) => snapshots.push(snapshot),
  onStale: () => void = () => undefined,
  onProtocolError: (error: Error) => void = () => undefined,
): OverlayClient {
  const callbacks: OverlayClientCallbacks = {
    onSnapshot,
    onStale,
    onProtocolError,
    onDiagnostic: () => undefined,
  };
  const dependencies: OverlayClientDependencies = {
    fetchState: fetchState as typeof fetch,
    createEventSource: () => source,
    setTimer: setTimeout,
    clearTimer: clearTimeout,
  };
  return new OverlayClient(
    { stateUrl: "/api/v1/state", eventsUrl: "/api/v1/events", staleAfterMs: 5_000 },
    callbacks,
    dependencies,
  );
}

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

async function flushPromises(): Promise<void> {
  for (let index = 0; index < 5; index += 1) {
    await Promise.resolve();
  }
}
