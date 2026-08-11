import "./styles/main.css";
import { config } from "./config";
import { ArtworkLoader } from "./services/artwork-loader";
import { OverlayClient } from "./services/overlay-client";
import {
  createInitialState,
  markConnectionStale,
  reduceSnapshot,
  type NowPlayingClientState,
  type WidgetViewState,
} from "./state/now-playing-reducer";
import { NowPlayingWidget } from "./ui/widget";

const widget = new NowPlayingWidget();
const artwork = new ArtworkLoader(widget, undefined, undefined, config.artworkGraceMs);
let state = createInitialState();

function commit(nextState: NowPlayingClientState): void {
  const previousView = state.view;
  state = nextState;
  render(previousView, nextState.view);
}

function render(previous: WidgetViewState, next: WidgetViewState): void {
  const textChanged = previous.textRevision !== next.textRevision;
  if (previous.artworkRevision !== next.artworkRevision) {
    // A new track starts a short visual grace period so a shared cover can remain stable.
    void artwork.update(next.artworkUrl, textChanged);
  }
  if (textChanged) {
    widget.updateText({ artist: next.artist, track: next.track });
  }
  if (previous.visible !== next.visible) {
    if (next.visible) {
      widget.show();
    } else {
      widget.hide();
    }
  }
}

const client = new OverlayClient(
  {
    stateUrl: config.stateUrl,
    eventsUrl: config.eventsUrl,
    staleAfterMs: config.connectionStaleAfterMs,
  },
  {
    onSnapshot: (snapshot) => commit(reduceSnapshot(state, snapshot)),
    onStale: () => commit(markConnectionStale(state)),
    onProtocolError: (error) => console.error("Unsupported now-playing protocol state.", error),
    onDiagnostic: (message, error) => console.warn(message, error),
    onServerEndpointChange: (overlayUrl) => window.location.replace(overlayUrl),
  },
);

client.start();
window.addEventListener("pagehide", () => client.stop(), { once: true });
