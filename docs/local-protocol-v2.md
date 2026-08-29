# Local protocol version 2

> Status: historical version 2 contract. `dev@730d5da` remains the last v2 baseline. Version 3 is the current runtime contract documented in [`local-protocol-v3.md`](./local-protocol-v3.md); this file is retained for history only and does not describe a compatibility runtime.

This document freezes the local contract shared by the host and browser client. The service listens only on `127.0.0.1`; the default port is `13130`.

## Endpoints

| Method | Path | Cache policy | Purpose |
| --- | --- | --- | --- |
| `GET`, `HEAD` | `/NowPlaying.html` | `no-store` | Embedded production overlay page |
| `GET`, `HEAD` | `/api/v2/state` | `no-store` | Latest complete state snapshot |
| `GET`, `HEAD` | `/api/v2/appearance` | `no-store` | Complete effective presentation configuration read once at page load |
| `GET` | `/api/v2/events` | `no-store` | Server-Sent Events containing complete state snapshots |
| `GET`, `HEAD` | `/api/v2/artwork/{artworkId}` | one year, `immutable` | Content-addressed PNG, JPEG, or WebP bytes |
| `GET` | `/oauth/spotify/callback` | `no-store` | One-time callback for a pending Spotify PKCE authorization |
| `GET`, `HEAD` | `/health` | `no-store` | Host and media-source readiness without track metadata |

Only the Host header `127.0.0.1`, with an optional port, is accepted. CORS and forwarded headers are not enabled.

## State shape

The C# DTOs in `host/Protocol` and TypeScript definitions in `web/src/protocol.ts` represent the same version 2 shape:

```json
{
  "protocolVersion": 2,
  "serverInstanceId": "f64b0c0f-73f3-4c0c-8b76-e84b89b77db2",
  "snapshotRevision": 42,
  "source": {
    "provider": "windows-media"
  },
  "playback": "playing",
  "track": {
    "title": "Track title",
    "artist": "Artist name",
    "albumTitle": "Album name",
    "albumArtist": null,
    "subtitle": null,
    "trackNumber": 3,
    "albumTrackCount": 12,
    "playbackType": "music",
    "genres": ["Rock", "Pop"]
  },
  "artwork": {
    "artworkRevision": 7,
    "artworkId": "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a",
    "url": "/api/v2/artwork/9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a"
  },
  "observedAt": "2026-08-09T12:00:00Z"
}
```

`source` is either null or an object containing only `provider`. The host emits `windows-media` or `spotify-api` for the active provider. Exact AUMIDs, friendly names, accounts, Client IDs, and devices never enter this DTO. Null source means no source is configured; a selected but unavailable provider keeps its provider value with `playback: "unavailable"`.

The state matrix is:

- `playing` requires source and track.
- `paused` and `stopped` require source; track may be null.
- `idle` requires source and null track/artwork.
- `unavailable` requires null track/artwork; source may be null only when unconfigured.
- Artwork always requires track.

Optional track fields are explicit nulls, while `genres` is always an array. Playback values are `playing`, `paused`, `stopped`, `idle`, or `unavailable`; playback types are `unknown`, `music`, `video`, `image`, or null.

`serverInstanceId` changes on each host start. A client compares `snapshotRevision` only within the same instance and resets its baseline when the instance changes.

## Appearance shape

Appearance is a separate, low-frequency presentation contract. It is not part of the state DTO, does not increment `snapshotRevision`, and is not sent over SSE:

```json
{
  "appearanceVersion": 3,
  "preset": "default",
  "artistColor": "#25C7A0",
  "trackColor": "#FFFFFF",
  "backgroundColor": "#1B1D20",
  "backgroundOpacityPercent": 100,
  "cornerRadius": 0,
  "fontFamily": null,
  "artistFontSize": 16,
  "artistFontWeight": 600,
  "trackFontSize": 22,
  "trackFontWeight": 700,
  "artworkVisible": true,
  "artworkSize": 70,
  "artworkPosition": "left",
  "artworkFit": "contain",
  "artworkCornerRadius": 0
}
```

The object contains exactly these fields. `appearanceVersion` is independent from the now-playing protocol version; version 2 added bounded typography and version 3 adds bounded artwork composition while retaining the same endpoint. Colors use canonical uppercase `#RRGGBB`; opacity is an integer from `0` to `100`, and corner radii are integers from `0` to `35` logical pixels. `fontFamily` is null for the product font stack or a bounded system font name. Artist size is `12`–`18`, track size is `16`–`24`, and weights are `400`, `500`, `600`, or `700`; line heights are derived by the page and are not protocol fields. Artwork size is `40`–`70` logical pixels, position is `left` or `right`, and fit is `contain` or `cover`. Visibility only changes presentation: artwork acquisition, content addressing, cancellation, and latest-wins behavior are unchanged. `preset` is `default` or `custom`, while all remaining fields always contain the final effective values. The page reads the endpoint once before starting the now-playing client and falls back to its built-in Default values if the request or validation fails. Saved changes therefore require a page reload and never change source or playback state.

## Event stream

Each state event is a complete snapshot:

```text
event: state
id: <serverInstanceId>:<snapshotRevision>
data: <version 2 JSON snapshot>
```

The initial event always contains the current state. `Last-Event-ID` is diagnostic only; the host does not replay history. Heartbeats are SSE comments and do not change the revision. Each client subscription has capacity one, so a slow client retains only the newest complete snapshot, and the host enforces a separate total SSE connection limit.

When a running user changes the loopback port, an already loaded page receives this control event before the old listener retires:

```text
event: server
data: {"overlayUrl":"http://127.0.0.1:13130/NowPlaying.html"}
```

The production client navigates only when `overlayUrl` is an absolute `http://127.0.0.1:<port>/NowPlaying.html` URL without credentials, query, or fragment. Invalid control events are ignored without invalidating the last good state snapshot. This event does not rewrite the URL saved in OBS, which the user must update for future reloads and OBS restarts.

## Health shape

`/health` reports host lifecycle plus provider-neutral source state. It includes `activeSourceProvider` (`windows-media`, `spotify-api`, or null) and `sourceStatus` (`unconfigured`, `starting`, `available`, `unavailable`, or `faulted`). It does not expose AUMIDs, media text, exceptions, Client IDs, or Spotify credentials.

The `/api/v1/*` routes do not exist. Changing this contract requires a new protocol version or a reviewed backward-compatible extension across both DTO implementations and their contract tests. The accepted v2-to-v3 transition is a breaking, atomic host/browser upgrade with no dual-protocol runtime support.
