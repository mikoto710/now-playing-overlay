# Local protocol version 1

This document freezes the local contract shared by the host and browser client. The service listens only on `127.0.0.1`; the default port is `10598`.

## Endpoints

| Method | Path | Cache policy | Purpose |
| --- | --- | --- | --- |
| `GET`, `HEAD` | `/NowPlaying.html` | `no-store` | Diagnostic page in M3; replaced by the production overlay later |
| `GET`, `HEAD` | `/api/v1/state` | `no-store` | Latest complete state snapshot |
| `GET` | `/api/v1/events` | `no-store` | Server-Sent Events containing complete state snapshots |
| `GET`, `HEAD` | `/api/v1/artwork/{artworkId}` | one year, `immutable` | Content-addressed PNG, JPEG, or WebP bytes |
| `GET`, `HEAD` | `/health` | `no-store` | Host and media-source readiness without track metadata |

Only the Host header `127.0.0.1`, with an optional port, is accepted. CORS and forwarded headers are not enabled.

## State shape

The C# DTOs in `host/Protocol` and TypeScript definitions in `web/src/protocol.ts` represent the same version 1 shape:

```json
{
  "protocolVersion": 1,
  "serverInstanceId": "f64b0c0f-73f3-4c0c-8b76-e84b89b77db2",
  "snapshotRevision": 42,
  "source": "spotify",
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
    "url": "/api/v1/artwork/9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a"
  },
  "observedAt": "2026-08-09T12:00:00Z"
}
```

`source`, `track`, and `artwork` are explicit JSON nulls when unavailable. Optional track fields are also explicit nulls, while `genres` is always an array. Playback values are `playing`, `paused`, `stopped`, `idle`, or `unavailable`; playback types are `unknown`, `music`, `video`, `image`, or null.

`serverInstanceId` changes on each host start. A client compares `snapshotRevision` only within the same instance and resets its baseline when the instance changes.

## Event stream

Each state event is a complete snapshot:

```text
event: state
id: <serverInstanceId>:<snapshotRevision>
data: <version 1 JSON snapshot>
```

The initial event always contains the current state. `Last-Event-ID` is diagnostic only; the host does not replay history. Heartbeats are SSE comments and do not change the revision. Each client subscription has capacity one, so a slow client retains only the newest complete snapshot, and the host enforces a separate total SSE connection limit.

When a running user changes the loopback port, an already loaded page can receive this backward-compatible control event before the old listener retires:

```text
event: server
data: {"overlayUrl":"http://127.0.0.1:13130/NowPlaying.html"}
```

The production client navigates only when `overlayUrl` is an absolute `http://127.0.0.1:<port>/NowPlaying.html` URL without credentials, query, or fragment. Invalid control events are ignored without invalidating the last good state snapshot. Older version 1 clients listen only for named `state` events and therefore ignore `server`; this extension does not change the state DTO or revision rules. It also does not rewrite the URL saved in OBS, which the user must update for future reloads and OBS restarts.

Changing this contract requires a new protocol version or a backward-compatible extension reviewed against both DTO implementations and their contract tests.
