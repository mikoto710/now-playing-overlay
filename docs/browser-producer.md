# Browser Producer

> Status: PC-03 implementation, committed locally on `protocol-v3` and ready for acceptance. The branch has not been pushed.

`NowPlayingOverlay.user.js` is the ordinary-user entry point for Custom Source. It is one standalone Tampermonkey userscript shipped in the same release ZIP as the Host. There is no second executable, bridge service, SDK, npm package, or separate project.

## Install and connect

1. Install Tampermonkey in the browser used for playback.
2. Start `NowPlayingOverlay.exe`.
3. Open tray **Settings...** and choose **Custom Source**.
4. Select **Install Browser Producer...** and confirm installation in Tampermonkey.
5. Select **Copy Connection Code**.
6. Open a supported player page. In Tampermonkey's userscript menu, choose **Configure Now Playing Overlay** and paste the code.
7. Save **Custom Source** as the active provider and start playback.

The connection code has the opaque form `npo1:<port>:<key>`. It is configuration, not a URL. Do not publish it or paste it into OBS. If the Host port changes, copy the code again.

## Current site boundary

The userscript has explicit matches for:

- Spotify Web;
- YouTube Music and YouTube;
- SoundCloud;
- Deezer;
- Yandex Music;
- Pretzel;
- Plex Web;
- Chillhop;
- Bilibili.

There is no unrestricted all-sites match. The first adapter reads `navigator.mediaSession` plus the active page audio/video element to produce `playing`, `paused`, `stopped`, or `idle` and the available title, artist, and album. A matched site that does not expose useful Media Session metadata will remain idle until a reviewed site adapter is added.

## Lifecycle and multi-tab behavior

The userscript automatically:

- creates one stable Producer ID in private userscript storage;
- persists a strictly increasing revision across page reloads and Host restarts;
- sends state changes and periodic heartbeats to `127.0.0.1`;
- retries at a bounded interval after connection failures, Host restart, or lease conflict;
- elects one tab across all matched sites for this userscript;
- prefers an actively playing tab and keeps an existing paused/stopped owner sticky;
- removes its candidacy when the page closes, allowing Host TTL to derive unavailable state.

Only the elected tab sends metadata. A background paused or stopped tab cannot replace a current owner. When another tab begins playing, it becomes the deterministic leader. The Host still enforces one Producer lease, strict revision ordering, conflict rejection, and a 10-second expiry independently of the browser election.

## Connection and security

The script stores the connection code in Tampermonkey storage and sends the key only in the `Authorization` header. It never places the key in a page URL or in the distributed source file. The Host listens only on IPv4 loopback, validates the exact Host header, exposes no CORS opt-in, accepts JSON only, bounds body size and request rate, and stores the key with Windows DPAPI for the current user.

**Rotate Code...** immediately replaces the persisted key, revokes the current lease, and copies the new connection code. Every existing script configuration then fails closed with `401` until the new code is pasted.

## Troubleshooting

- Use the userscript menu **Show Now Playing Overlay status** to confirm the configured port and whether the tab owns the lease.
- If the Host port changed, copy and paste a new connection code.
- If code rotation occurred, paste the newly copied code.
- If the page is matched but no track appears, check whether `navigator.mediaSession.metadata` is populated in that site/browser combination.
- If another tab is actively playing, that tab intentionally owns the overlay.
- Open the Host logs from the tray only for Host-side failures; the userscript writes concise warnings to the browser console for rejected codes.

## Adding a site adapter

Adapters are metadata readers, not protocol clients. A new adapter belongs before the Media Session fallback and implements two operations:

```javascript
{
    id: "site-name",
    matches: () => location.hostname === "music.example.com",
    read: () => ({
        playback: "playing", // playing | paused | stopped | idle
        title: "Track title",
        artist: "Artist",
        albumTitle: "Album", // optional
        trackId: "stable-site-id", // optional
    }),
}
```

The fixed transport continues to normalize text, authenticate, assign `producerId` and `producerRevision`, retry, heartbeat, and coordinate tabs. Do not duplicate those responsibilities in an adapter. Do not send `timeline`, artwork, remote URLs, account data, or playback controls: ingest v1 rejects fields outside its frozen shape.

After adding a match or adapter, extend `integrations/tests/browser-producer.test.js`, run `node --test integrations/tests/browser-producer.test.js`, then run the repository check and a real Tampermonkey-to-Host browser acceptance.
