# Local protocol version 3

> Status: current implemented and accepted Phase A contract. PA-01 is committed at `dev@730d5da`; PA-02 and the PA-03 closeout remain uncommitted and unpushed. [`local-protocol-v2.md`](./local-protocol-v2.md) is retained only as the historical v2 contract.

This document freezes the next local contract shared by the host and embedded browser client. The service remains bound only to `127.0.0.1`; the default port remains `13130`.

## Atomic endpoint namespace

Phase A upgraded the host, embedded page, TypeScript parser, fixtures, and HTTP/contract tests as one unit:

| Method | Path | Cache policy | Purpose |
| --- | --- | --- | --- |
| `GET`, `HEAD` | `/NowPlaying.html` | `no-store` | Embedded production overlay page |
| `GET`, `HEAD` | `/api/v3/state` | `no-store` | Latest complete state snapshot |
| `GET`, `HEAD` | `/api/v3/appearance` | `no-store` | Complete effective presentation configuration read once at page load |
| `GET` | `/api/v3/events` | `no-store` | Server-Sent Events containing complete state snapshots |
| `GET`, `HEAD` | `/api/v3/artwork/{artworkId}` | one year, `immutable` | Content-addressed PNG, JPEG, or WebP bytes |
| `GET` | `/oauth/spotify/callback` | `no-store` | One-time callback for a pending Spotify PKCE authorization |
| `GET`, `HEAD` | `/health` | `no-store` | Host and media-source readiness without track metadata |

Every `/api/v2/*` route now returns `404`. There is no redirect, compatibility fallback, content negotiation, or v2/v3 dual handler. The browser parser rejects a state payload with `protocolVersion: 2`.

`/health`, `/NowPlaying.html`, and the Spotify OAuth callback are intentionally outside the versioned `/api/v3` namespace. Loopback binding, strict Host validation, no CORS, and no forwarded-header trust remain unchanged.

## State shape

The C# DTOs and TypeScript parser represent the same version 3 shape:

```json
{
  "protocolVersion": 3,
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
  "timeline": {
    "positionMs": 100000,
    "durationMs": 240000,
    "sampledAt": "2026-08-19T03:00:00.000Z"
  },
  "artwork": {
    "artworkRevision": 7,
    "artworkId": "9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a",
    "url": "/api/v3/artwork/9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a"
  },
  "observedAt": "2026-08-19T03:00:00.100Z"
}
```

`timeline` is nullable. Phase A established the contract while all currently implemented sources continue to emit `null`.

## Provider token

The host keeps a strong internal `SourceProvider` enum. The output `source.provider` is a bounded, canonical token rather than a closed browser union. Current and planned canonical values are:

```text
windows-media
spotify-api
window-title
external-push
```

A token is 1 to 64 ASCII characters and must match:

```text
^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$
```

The token therefore starts with a lowercase letter, contains only lowercase letters, digits, and single internal hyphens, and cannot contain control characters. The length bound and grammar are shared by the host and browser. The browser accepts a future token that satisfies this contract instead of rejecting the complete snapshot merely because the provider is not yet known to its UI.

The full `SourceKey`, instance ID, AUMID, source ID, producer ID, display name, Spotify account, and Client ID remain host-internal.

## Playback timeline

`PlaybackTimeline` is independent from `TrackMetadata`:

```text
positionMs
durationMs
sampledAt
```

The fields form one measurement anchor: `positionMs` is the estimated media position at UTC `sampledAt`. A source that cannot express that relationship reliably emits `timeline: null` rather than assigning a convenient but false timestamp.

Domain invariants are:

```text
positionMs >= 0
durationMs > 0
positionMs <= durationMs
```

A source may clamp a reasonable boundary race. A timeline never carries over to a different track.

The playback matrix is:

| Playback state | Timeline |
| --- | --- |
| `playing` | May be present |
| `paused` | May be present |
| `stopped` | Must be null |
| `idle` | Must be null |
| `unavailable` | Must be null |

## Revision semantics

Timeline does not use raw record equality:

- `null` to `null` does not create a revision.
- `null` to available, or available to `null`, creates a revision.
- While paused, unchanged position and duration with only a new `sampledAt` does not create a revision; a position or duration change does.
- While playing, the host projects the previous anchor to the candidate `sampledAt`. A difference within the internal tolerance is semantically equal; a larger correction or seek creates a revision.
- Track, source, or playback-state changes create a revision.
- `observedAt` alone remains outside visible-state equality.
- Browser-side extrapolation never creates a host revision.

The current domain implementation starts with an internal `500 ms` tolerance and corresponding tests. It is not a protocol field, public contract, or permanent architecture constant. Phase D revalidates it using GSMTC and Spotify latency, polling jitter, pause, and seek evidence. No dynamic-tolerance framework is required.

A semantically equal new anchor does not need to replace the published snapshot solely because `sampledAt` changed. If another real metadata or state change causes a commit, the newest trustworthy anchor may be included in that new snapshot.

## Rendering and sampling frequencies

The following frequencies are independent:

```text
Browser render frequency
!= Source sampling frequency
!= Spotify API polling frequency
```

Timeline is a sampled anchor, not a host tick stream. The host does not publish high-frequency snapshots for a progress bar. A future browser can extrapolate from position, sampled time, and playback state, but a progress bar and RAF animation are not part of Phase A.

## Artwork

`artworkId` remains a 64-character lowercase SHA-256 content hash. `artwork.url` must be exactly `/api/v3/artwork/{artworkId}`. The byte endpoint, DTO mapper, browser parser, embedded page, and tests move atomically; `/api/v2/artwork/{artworkId}` is not retained.

## Appearance

Appearance remains a separate, low-frequency presentation DTO that is loaded once and is not part of now-playing state or SSE.

The `v3` in `/api/v3/appearance` is the Local HTTP Protocol namespace. `appearanceVersion` is the independent schema version of the Appearance DTO. Equality between those numbers is not an invariant. For example, this is valid without a Local Protocol upgrade:

```text
GET /api/v3/appearance

{
  "appearanceVersion": 4,
  ...
}
```

Appearance failures continue to fall back to the embedded Default presentation without changing source, playback, or snapshot revision.

## Event stream

Each `state` event remains a complete version 3 snapshot:

```text
event: state
id: <serverInstanceId>:<snapshotRevision>
data: <version 3 JSON snapshot>
```

The initial event contains current state. `Last-Event-ID` remains diagnostic only; there is no history replay. Heartbeats are SSE comments and do not alter revision. Capacity-one latest-state behavior and the existing `server` endpoint-change control event remain unchanged.

## Health

`/health` remains unversioned. `activeSourceProvider` uses the same canonical serializer as `source.provider`, including future `window-title` and `external-push` values when those providers are actually implemented.

Health never exposes instance ID, external source ID, producer ID, display name, track metadata, Spotify account, Spotify Client ID, or `IngestKey`. Future enum values are not added to production merely to satisfy this document; each provider adds and tests its health token in its implementation phase.

## External input is a separate protocol

External Push input does not POST this state DTO. Its future `/ingest/v1/state` and `/ingest/v1/heartbeat` contract is independently versioned and cannot control server instance, snapshot revision, observed time, `SourceKey`, or host artwork identity. External Push is outside Phase A.

## Implemented Phase A boundary

Phase A was contract-first. It added the timeline domain and pass-through, version 3 DTOs and routes, provider-token validation, browser parser/config, a timeline pure helper, atomic embedded-page output, protocol documentation, and canonical health serialization coverage.

It did not implement GSMTC or Spotify timelines, richer Spotify metadata, polling changes, WindowTitle, ExternalPush, `IngestKey`, ingest POST routes, a progress bar, RAF animation, artwork enhancements, adapters, or source-settings migration. Those capabilities remain subject to their later phases and separate authorization.
