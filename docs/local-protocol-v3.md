# Local protocol version 3

> Status: current output protocol and Browser Player input contract as of v0.3.0. Phase A, Phase B, PC-01/PC-02/PC-03, and ART-01/ART-02/ART-03 are implemented and accepted. [`local-protocol-v2.md`](./local-protocol-v2.md) is historical only.

This document freezes the next local contract shared by the host and embedded browser client. The service remains bound only to `127.0.0.1`; the default port remains `13130`.

## Atomic endpoint namespace

Phase A upgraded the host, embedded page, TypeScript parser, fixtures, and HTTP/contract tests as one unit:

| Method | Path | Cache policy | Purpose |
| --- | --- | --- | --- |
| `GET`, `HEAD` | `/NowPlaying.html` | `no-store` | Embedded production overlay page |
| `GET`, `HEAD` | `/NowPlayingOverlay.user.js` | `no-store` | Official Host-embedded Tampermonkey Browser Producer |
| `GET`, `HEAD` | `/api/v3/state` | `no-store` | Latest complete state snapshot |
| `GET`, `HEAD` | `/api/v3/appearance` | `no-store` | Complete effective presentation configuration read once at page load |
| `GET` | `/api/v3/events` | `no-store` | Server-Sent Events containing complete state snapshots |
| `GET`, `HEAD` | `/api/v3/artwork/{artworkId}` | one year, `immutable` | Content-addressed PNG, JPEG, or WebP bytes |
| `GET` | `/oauth/spotify/callback` | `no-store` | One-time callback for a pending Spotify PKCE authorization |
| `GET`, `HEAD` | `/health` | `no-store` | Host and media-source readiness without track metadata |
| `POST` | `/ingest/v1/state` | `no-store` | Authenticated Browser Player state claim or update |
| `POST` | `/ingest/v1/heartbeat` | `no-store` | Authenticated renewal for the current Producer lease |
| `POST` | `/ingest/v1/artwork` | `no-store` | Authenticated raw artwork bytes bound to the current state revision |

Every `/api/v2/*` route now returns `404`. There is no redirect, compatibility fallback, content negotiation, or v2/v3 dual handler. The browser parser rejects a state payload with `protocolVersion: 2`.

`/health`, the two embedded assets, and the Spotify OAuth callback are intentionally outside the versioned `/api/v3` namespace. `/ingest/v1` is an independently versioned input contract; it is not an extension of the output DTO and does not imply output protocol v1. Loopback binding, strict Host validation, no CORS, and no forwarded-header trust remain unchanged.

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

The host keeps a strong internal `SourceProvider` enum. The output `source.provider` is a bounded, canonical token rather than a closed browser union. Current production canonical values are:

```text
windows-media
spotify-api
external-push
window-title
```

`window-title` identifies metadata read from a selected desktop window caption. The full target identity, executable path, window class, and current raw caption remain Host-internal.

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

`/health` remains unversioned. `activeSourceProvider` uses the same canonical serializer as `source.provider`; current non-null values are `windows-media`, `spotify-api`, `external-push`, and `window-title`.

Health never exposes instance ID, external source ID, producer ID, display name, track metadata, Spotify account, Spotify Client ID, or `IngestKey`. Each provider adds and tests its health token only with its production implementation.

## Browser Player input protocol

Browser Player is the user-facing name for the fixed browser integration and is serialized to output as `external-push`. `ExternalPush` remains the internal provider/transport identity so future non-browser Producers can reuse the same low-level contract without changing persisted settings or the output protocol. External input never posts the output state DTO. A Producer cannot control `serverInstanceId`, `snapshotRevision`, `observedAt`, `SourceKey`, output artwork identity, appearance, or the Host URL.

Ordinary users should use the official [Browser Producer](./browser-producer.md). Direct `/ingest/v1` calls are an advanced integration surface.

### Authentication and transport

All three endpoints require:

```http
Authorization: Bearer <43-character base64url IngestKey>
```

State and heartbeat bodies additionally require `Content-Type: application/json; charset=utf-8`. Artwork uses its image media type and revision-binding headers as described below.

The Host generates a 32-byte random key on first use, protects it with Windows DPAPI `CurrentUser`, and stores the protected document at `%LOCALAPPDATA%\NowPlayingOverlay\ingest-key.dat`. The key is never accepted in the URL. Settings exposes only a versioned connection code, `npo1:<port>:<key>`, for copying into the official userscript. Rotating the code atomically replaces the persisted key, rejects the old key immediately, and revokes the current lease.

Only `127.0.0.1` is bound. Exact Host validation, no CORS opt-in, no forwarded-header trust, a five-second body-read timeout, strict content encoding, and bounded request concurrency apply to all ingest routes. JSON state/heartbeat requests additionally have a 16 KiB body limit, depth limit 8, strict property names, duplicate-property rejection, unknown-field rejection, and a shared 20-request/one-second rate limit. Track text is normalized by the Host and capped at 512 Unicode scalars. Artwork has its own byte, dimension, and rate limits.

### State

```http
POST /ingest/v1/state
```

```json
{
  "producerId": "f5b7d897-c655-4cdf-a93b-cd10bd0707d7",
  "producerRevision": 42,
  "playback": "playing",
  "track": {
    "title": "Track title",
    "artist": "Artist",
    "albumTitle": "Album",
    "trackId": "provider-stable-id"
  }
}
```

`producerId` must be a non-empty UUID. `producerRevision` is a positive signed 64-bit integer and must increase strictly for the current Producer. `playback` is exactly `playing`, `paused`, `stopped`, or `idle`; numeric enums and `unavailable` are rejected. `track` is nullable, but if present its non-empty normalized `title` is required; `artist`, `albumTitle`, and `trackId` are optional strings.

The input state matrix is:

| Playback | Track |
| --- | --- |
| `playing` | Required |
| `paused` | Optional |
| `stopped` | Optional |
| `idle` | Must be null |

`timeline`, an `artwork` JSON property, remote URLs, playback controls, account data, and provider-specific fields are not state fields. Strict JSON rejects them instead of accepting and ignoring them. Artwork uses the separate byte endpoint below.

### Artwork

```http
POST /ingest/v1/artwork
Authorization: Bearer <IngestKey>
Content-Type: image/png | image/jpeg | image/webp
X-NPO-Producer-Id: f5b7d897-c655-4cdf-a93b-cd10bd0707d7
X-NPO-Producer-Revision: 42

<raw image bytes>
```

The Producer must first receive `204` for a state containing a track, then upload artwork for exactly that Producer ID and accepted state revision. The Host rechecks the active owner, exact revision, track presence, and current key generation after reading the body. Artwork does not claim or renew the lease. A newer state with the same track identity retains the accepted artwork; a changed track identity, idle state, expiry, revocation, or Source switch clears it.

The body is raw PNG, JPEG, or WebP bytes, not JSON, base64, multipart data, or a URL. The declared media type must match the detected bytes. The Host accepts at most 5 MiB, 4096 pixels on either dimension, and 16,777,216 total pixels, with a separate limit of four artwork requests per ten seconds. Accepted bytes enter the existing `IArtworkReader -> ArtworkCache` path and are exposed only through the content-addressed `/api/v3/artwork/{artworkId}` output URL. The Host never fetches a Producer-controlled remote artwork URL.

### Heartbeat and lease

```http
POST /ingest/v1/heartbeat
```

```json
{
  "producerId": "f5b7d897-c655-4cdf-a93b-cd10bd0707d7"
}
```

The first accepted state claims the single Producer lease. The same Producer may update it only with a strictly greater revision. Its heartbeat renews ownership without changing the published media state or snapshot revision. A foreign state/heartbeat, stale revision, replay, or heartbeat without an active lease returns conflict and does not renew ownership.

The production lease duration is 10 seconds, measured only with Host monotonic time. On expiry the Host clears the owner and state, publishes Browser Player as unavailable, and allows another Producer to claim. A Producer cannot submit `unavailable`. Host restart begins with no lease; the persisted key remains valid and the Producer must resend state.

### Responses

| Status | Meaning |
| --- | --- |
| `204` | State or artwork accepted, or owner heartbeat renewed |
| `400` | Invalid JSON/state value, artwork target header, empty/invalid image, or declared/detected image mismatch |
| `401` | Missing or invalid bearer key |
| `405` | Method other than `POST`; `Allow: POST` |
| `408` | Body read timed out |
| `409` | Stale/replayed state, wrong Producer/revision, missing current track, or no active owner |
| `413` | JSON body exceeds 16 KiB or artwork exceeds 5 MiB |
| `415` | Unsupported content type, charset, or content encoding |
| `429` | JSON or separate artwork rate limit exceeded; includes `Retry-After` |

Common Host gates may also reject an invalid Host header, excessive headers, or exhausted concurrent-request capacity before the ingest handler runs. Responses contain no state or credential body and use `Cache-Control: no-store`.

## Implemented phase boundary

Phase A added Timeline domain/pass-through and output protocol v3. Phase B made source configuration provider-neutral. PC-01 added strict external state and the single-owner lease; PC-02 added the protected IngestKey and fail-closed HTTP transport; PC-03 connects the production Source, Settings/Shell, application composition, embedded userscript asset, release package, and official site-aware Browser Producer.

ART-01 added revision-bound artwork state and the External artwork reader; ART-02 added the authenticated raw-byte route and bounded Host validation; ART-03 added site-aware browser extraction, browser-side retrieval, and byte upload after state acceptance. These changes do not add a remote URL to either ingest state or output state, and they do not add Timeline, a progress UI, playback controls, source registry, multiple logical sources, Producer winner arbitration, a new SDK/package/project, a bridge executable, or a WindowTitle Source.
