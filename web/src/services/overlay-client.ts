import { parseNowPlayingState, type NowPlayingStateDto } from "../protocol";

export interface EventSourceLike {
  onerror: ((event: Event) => void) | null;
  addEventListener(type: "state", listener: (event: { data: string }) => void): void;
  close(): void;
}

export interface OverlayClientCallbacks {
  onSnapshot(snapshot: NowPlayingStateDto): void;
  onStale(): void;
  onProtocolError(error: Error): void;
  onDiagnostic(message: string, error?: unknown): void;
}

export interface OverlayClientDependencies {
  fetchState: typeof fetch;
  createEventSource: (url: string) => EventSourceLike;
  setTimer: typeof setTimeout;
  clearTimer: typeof clearTimeout;
}

export interface OverlayClientOptions {
  stateUrl: string;
  eventsUrl: string;
  staleAfterMs: number;
}

export class OverlayClient {
  private eventSource: EventSourceLike | null = null;
  private staleTimer: ReturnType<typeof setTimeout> | null = null;
  private started = false;
  private failed = false;

  constructor(
    private readonly options: OverlayClientOptions,
    private readonly callbacks: OverlayClientCallbacks,
    private readonly dependencies: OverlayClientDependencies = {
      fetchState: window.fetch.bind(window),
      createEventSource: (url) => new EventSource(url) as unknown as EventSourceLike,
      setTimer: window.setTimeout.bind(window),
      clearTimer: window.clearTimeout.bind(window),
    },
  ) {
    if (options.staleAfterMs <= 0) {
      throw new Error("staleAfterMs must be positive.");
    }
  }

  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;

    try {
      this.eventSource = this.dependencies.createEventSource(this.options.eventsUrl);
      this.eventSource.addEventListener("state", (event) => this.handleEvent(event.data));
      this.eventSource.onerror = () => this.scheduleStaleState();
    } catch (error) {
      this.callbacks.onDiagnostic("Unable to open the now-playing event stream.", error);
      this.scheduleStaleState();
    }

    void this.loadInitialState();
  }

  stop(): void {
    this.started = false;
    this.failed = true;
    this.eventSource?.close();
    this.eventSource = null;
    this.clearStaleTimer();
  }

  private async loadInitialState(): Promise<void> {
    try {
      const response = await this.dependencies.fetchState(this.options.stateUrl, {
        cache: "no-store",
        headers: { Accept: "application/json" },
      });
      if (!response.ok) {
        throw new Error(`Initial state request failed (${response.status}).`);
      }
      let value: unknown;
      try {
        value = await response.json();
      } catch (error) {
        this.failProtocol(toError(error));
        return;
      }
      this.handleValue(value);
    } catch (error) {
      if (!this.failed) {
        this.callbacks.onDiagnostic("Unable to load the initial now-playing state.", error);
      }
    }
  }

  private handleEvent(data: string): void {
    if (this.failed) {
      return;
    }

    try {
      this.handleValue(JSON.parse(data) as unknown);
    } catch (error) {
      this.failProtocol(toError(error));
    }
  }

  private handleValue(value: unknown): void {
    if (this.failed) {
      return;
    }

    try {
      const snapshot = parseNowPlayingState(value);
      this.clearStaleTimer();
      this.callbacks.onSnapshot(snapshot);
    } catch (error) {
      this.failProtocol(toError(error));
    }
  }

  private scheduleStaleState(): void {
    if (this.failed || this.staleTimer !== null) {
      return;
    }

    this.staleTimer = this.dependencies.setTimer(() => {
      this.staleTimer = null;
      if (!this.failed) {
        this.callbacks.onStale();
      }
    }, this.options.staleAfterMs);
  }

  private clearStaleTimer(): void {
    if (this.staleTimer === null) {
      return;
    }
    this.dependencies.clearTimer(this.staleTimer);
    this.staleTimer = null;
  }

  private failProtocol(error: Error): void {
    if (this.failed) {
      return;
    }
    this.failed = true;
    this.eventSource?.close();
    this.eventSource = null;
    this.clearStaleTimer();
    this.callbacks.onStale();
    this.callbacks.onProtocolError(error);
  }
}

function toError(error: unknown): Error {
  return error instanceof Error ? error : new Error(String(error));
}
