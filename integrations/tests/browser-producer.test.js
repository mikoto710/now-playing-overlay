import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const source = readFileSync(new URL("../NowPlayingOverlay.user.js", import.meta.url), "utf8");
const api = {};
vm.runInNewContext(source, { URL, __NOW_PLAYING_OVERLAY_TEST__: api });

function element(text = null, options = {}) {
    const classes = new Set(options.classes ?? []);
    return {
        textContent: text,
        innerText: text,
        paused: options.paused ?? true,
        ended: options.ended ?? false,
        readyState: options.readyState ?? 1,
        style: options.style ?? {},
        classList: {
            contains: value => classes.has(value),
            [Symbol.iterator]: () => classes.values(),
        },
        getAttribute: name => options.attributes?.[name] ?? null,
    };
}

function documentWith(selectors = {}, lists = {}, title = "") {
    return {
        title,
        querySelector: selector => selectors[selector] ?? null,
        querySelectorAll: selector => lists[selector] ?? [],
    };
}

function mediaContext(document, playbackState = "playing", artwork = []) {
    return {
        document,
        mediaSession: {
            playbackState,
            metadata: {
                title: "Media Title",
                artist: "Media Artist",
                album: "Media Album",
                artwork,
            },
        },
        locationHref: "https://example.test/",
    };
}

test("connection codes are strict and loopback-port bounded", () => {
    const token = "A".repeat(43);
    assert.deepEqual(
        { ...api.parseConnectionCode(`npo1:13130:${token}`) },
        { port: 13130, key: token });
    assert.equal(api.parseConnectionCode(`npo1:0:${token}`), null);
    assert.equal(api.parseConnectionCode(`npo1:65536:${token}`), null);
    assert.equal(api.parseConnectionCode(`http://127.0.0.1:13130:${token}`), null);
    assert.equal(api.parseConnectionCode(`npo1:13130:${token}=`), null);
});

test("adapter values are normalized to the ingest v1 shape", () => {
    assert.deepEqual(
        structuredClone(api.normalizeAdapterState({
            playback: "playing",
            title: "  Track\nTitle ",
            artist: " Artist ",
            albumTitle: " Album ",
            artworkUrl: "https://cdn.example.test/cover.jpg",
        })),
        {
            playback: "playing",
            track: {
                title: "Track Title",
                artist: "Artist",
                albumTitle: "Album",
                trackId: null,
            },
            artworkUrl: "https://cdn.example.test/cover.jpg",
        });
    assert.deepEqual(
        structuredClone(api.normalizeAdapterState({ playback: "playing" })),
        { playback: "idle", track: null });
    assert.equal(api.cleanArtworkUrl("javascript:alert(1)"), null);
});

test("artwork uploads use detected raw image bytes and state-bound headers", () => {
    const png = Uint8Array.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
    const jpeg = Uint8Array.from([0xff, 0xd8, 0xff, 0xd9]);
    const webp = Uint8Array.from([
        0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50,
    ]);

    assert.equal(api.detectArtworkContentType(png), "image/png");
    assert.equal(api.detectArtworkContentType(jpeg), "image/jpeg");
    assert.equal(api.detectArtworkContentType(webp), "image/webp");
    assert.equal(api.detectArtworkContentType(Uint8Array.from([1, 2, 3])), null);
    assert.equal(api.isApprovedRemoteArtworkUrl("https://i.scdn.co/image/cover"), true);
    assert.equal(api.isApprovedRemoteArtworkUrl("https://cdn.example.test/cover.jpg"), false);
    assert.equal(api.isApprovedRemoteArtworkUrl("http://i.scdn.co/image/cover"), false);

    const request = api.buildArtworkUploadRequest(
        { port: 13130, key: "secret" },
        "df3c450a-36b0-46ee-b708-879a5cbf2b08",
        42,
        png.buffer,
        "image/png");
    assert.equal(request.method, "POST");
    assert.equal(request.url, "http://127.0.0.1:13130/ingest/v1/artwork");
    assert.equal(request.data, png.buffer);
    assert.equal(request.headers["Content-Type"], "image/png");
    assert.equal(request.headers["X-NPO-Producer-Revision"], "42");
    assert.equal(Object.values(request.headers).includes("https://cdn.example.test/cover.jpg"), false);
});

test("Media Session sites preserve the page-provided title and fields", () => {
    const playingMedia = element(null, { paused: false });
    const document = documentWith({}, { "audio, video": [playingMedia] });
    const context = mediaContext(document, "playing", [
        { src: "https://cdn.example.test/small.jpg" },
        { src: "https://cdn.example.test/large.jpg" },
    ]);

    for (const hostname of ["open.spotify.com", "app.plex.tv"]) {
        assert.deepEqual(structuredClone(api.readSiteState(hostname, context)), {
            playback: "playing",
            title: "Media Title",
            artist: "Media Artist",
            albumTitle: "Media Album",
            artworkUrl: "https://cdn.example.test/large.jpg",
        });
    }

    const deezerDocument = documentWith(
        { '[data-testid="play_button_pause"]': element() },
        { "audio, video": [] });
    const deezer = api.readSiteState(
        "www.deezer.com",
        mediaContext(deezerDocument, "playing", [
            { src: "https://cdn.example.test/56x56-000000/cover.jpg" },
        ]));
    assert.equal(deezer.playback, "playing");
    assert.equal(deezer.artworkUrl, "https://cdn.example.test/512x512-000000/cover.jpg");

    const pretzelDocument = documentWith(
        { '[data-heapid="music-player"] [data-testid="play-button"]': element() },
        { "audio, video": [] });
    const pretzel = api.readSiteState(
        "play.pretzel.rocks",
        mediaContext(pretzelDocument, "playing", [
            { src: "https://cdn.example.test/medium.jpg" },
        ]));
    assert.equal(pretzel.playback, "stopped");
    assert.equal(pretzel.artworkUrl, "https://cdn.example.test/large.jpg");
});

test("SoundCloud and Yandex adapters read their current player elements", () => {
    const soundCloud = documentWith({
        ".playControl": element(null, { classes: ["playing"] }),
        ".playbackSoundBadge__titleLink": element(null, { attributes: { title: "Cloud Track" } }),
        ".playbackSoundBadge__lightLink": element(null, { attributes: { title: "Cloud Artist" } }),
        ".playbackSoundBadge span.sc-artwork": element(null, {
            style: { backgroundImage: 'url("https://cdn.example.test/t50x50/cover.jpg")' },
        }),
        "div.playlist.playing .soundTitle__title": element("Cloud Album"),
    });
    assert.deepEqual(structuredClone(api.readSiteState("soundcloud.com", { document: soundCloud })), {
        playback: "playing",
        title: "Cloud Track",
        artist: "Cloud Artist",
        albumTitle: "Cloud Album",
        artworkUrl: "https://cdn.example.test/t500x500/cover.jpg",
    });

    const yandex = documentWith({
        '[class*="VibePlayerControls_playButton"]': element(null, { classes: ["button_playing"] }),
        '[class*="VibePlayerbarMeta_trackNameText"]': element("Yandex Track"),
        '[class*="VibePage_text"]': element("Yandex Artist"),
        '[class*="AlbumCover_cover"] img': element(null, {
            attributes: { src: "https://cdn.example.test/100x100/cover.jpg" },
        }),
    });
    assert.deepEqual(structuredClone(api.readSiteState("music.yandex.com", { document: yandex })), {
        playback: "playing",
        title: "Yandex Track",
        artist: "Yandex Artist",
        artworkUrl: "https://cdn.example.test/400x400/cover.jpg",
    });
});

test("YouTube adapters use separate artist data instead of guessing title order", () => {
    const youtube = documentWith({
        "#text > a": element("Video Artist"),
        "#container > h1 > yt-formatted-string": element("Video Artist - Video Track (Official Audio)"),
        video: element(null, { paused: false }),
    });
    assert.deepEqual(structuredClone(api.readSiteState("www.youtube.com", {
        document: youtube,
        locationHref: "https://www.youtube.com/watch?v=abcdefghijk",
    })), {
        playback: "playing",
        title: "Video Track",
        artist: "Video Artist",
        artworkUrl: "https://i.ytimg.com/vi/abcdefghijk/maxresdefault.jpg",
    });
    assert.equal(api.cleanYouTubeTitle("Track Name", "Different Artist"), "Track Name");

    const artistSelector = [
        '.ytmusic-player-bar.byline [href*="channel/"]:not([href*="channel/MPREb_"]):not([href*="browse/MPREb_"])',
        '.ytmusic-player-bar.byline .yt-formatted-string:nth-child(2n+1):not([href*="browse/"]):not([href*="channel/"]):not(:nth-last-child(1)):not(:nth-last-child(3))',
        '.ytmusic-player-bar.byline [href*="browse/FEmusic_library_privately_owned_artist_detaila_"]',
    ].join(", ");
    const youtubeMusic = documentWith(
        {
            ".ytmusic-player-bar.title": element(null, { attributes: { title: "Music Track" } }),
            '.ytmusic-player-bar [href*="browse/MPREb_"]': element("Music Album"),
        },
        {
            [artistSelector]: [element("Artist One"), element("Artist Two")],
            "audio, video": [],
        });
    const context = mediaContext(youtubeMusic, "paused", [
        { src: "https://cdn.example.test/music-cover.jpg" },
    ]);
    assert.deepEqual(structuredClone(api.readSiteState("music.youtube.com", context)), {
        playback: "paused",
        title: "Music Track",
        artist: "Artist One, Artist Two",
        albumTitle: "Music Album",
        artworkUrl: "https://cdn.example.test/music-cover.jpg",
    });
});

test("Bilibili and Chillhop adapters retain their site-specific fallbacks", () => {
    const bilibili = documentWith(
        {
            "a.up-name": element("Uploader"),
            video: element(null, { paused: false }),
            'meta[property="og:image"]': element(null, {
                attributes: { content: "https://cdn.example.test/bilibili.jpg" },
            }),
        },
        { "audio, video": [element(null, { paused: false })] },
        "Fallback Video Title");
    const bilibiliState = api.readSiteState("www.bilibili.com", {
        document: bilibili,
        mediaSession: { playbackState: "playing", metadata: null },
        locationHref: "https://www.bilibili.com/video/BV1abc123/",
    });
    assert.deepEqual(structuredClone(bilibiliState), {
        playback: "playing",
        title: "Fallback Video Title",
        artist: "Uploader",
        trackId: "BV1abc123",
        artworkUrl: "https://cdn.example.test/bilibili.jpg",
    });

    const chillhop = documentWith({
        "#p-btn-play": element(null, { classes: ["playing"] }),
        ".p-track-title": element("Chill Track"),
        ".p-track-artist": element("Chill Artist"),
        ".player-image": element(null, {
            style: { backgroundImage: "url(https://cdn.example.test/chillhop.jpg)" },
        }),
    });
    assert.deepEqual(structuredClone(api.readSiteState("chillhop.com", { document: chillhop })), {
        playback: "playing",
        title: "Chill Track",
        artist: "Chill Artist",
        artworkUrl: "https://cdn.example.test/chillhop.jpg",
    });
    assert.equal(api.readSiteState("unsupported.example", { document: chillhop }), null);
});

test("the elected Producer fetches artwork and uploads bytes only after state acceptance", async () => {
    let now = 0;
    let tick;
    const requests = [];
    const storage = new Map([
        ["connectionCode", `npo1:13130:${"A".repeat(43)}`],
    ]);
    const media = element(null, { paused: false });
    media.addEventListener = () => {};
    const document = {
        ...documentWith({}, { "audio, video": [media] }),
        visibilityState: "visible",
        addEventListener: () => {},
    };
    const window = {
        location: {
            hostname: "open.spotify.com",
            href: "https://open.spotify.com/track/example",
        },
        addEventListener: () => {},
        setInterval: callback => {
            tick = callback;
            return 1;
        },
        clearInterval: () => {},
        prompt: () => null,
        alert: () => {},
    };
    class FakeDate extends Date {
        static now() {
            return now;
        }
    }
    const png = Uint8Array.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
    const request = options => {
        requests.push(options);
        queueMicrotask(() => {
            if (options.url === "https://i.scdn.co/image/runtime-cover.png") {
                options.onload({ status: 200, response: png.buffer });
            }
            else {
                options.onload({ status: 204 });
            }
        });
        return { abort: () => {} };
    };
    vm.runInNewContext(source, {
        URL,
        Date: FakeDate,
        console,
        crypto: { randomUUID: () => "f5b7d897-c655-4cdf-a93b-cd10bd0707d7" },
        document,
        navigator: {
            mediaSession: {
                playbackState: "playing",
                metadata: {
                    title: "Runtime Track",
                    artist: "Runtime Artist",
                    album: "Runtime Album",
                    artwork: [{ src: "https://i.scdn.co/image/runtime-cover.png" }],
                },
            },
        },
        window,
        GM_getValue: (key, fallback) => storage.has(key) ? storage.get(key) : fallback,
        GM_setValue: (key, value) => storage.set(key, value),
        GM_deleteValue: key => storage.delete(key),
        GM_listValues: () => [...storage.keys()],
        GM_registerMenuCommand: () => {},
        GM_xmlhttpRequest: request,
    });

    now = 2500;
    tick();
    await new Promise(resolve => setImmediate(resolve));
    const candidate = storage.get([...storage.keys()].find(key => key.startsWith("candidate:")));
    assert.equal(candidate.state.artworkUrl, "https://i.scdn.co/image/runtime-cover.png");
    now = 4000;
    tick();
    await new Promise(resolve => setImmediate(resolve));

    const stateRequests = requests.filter(value => value.url.endsWith("/ingest/v1/state"));
    const remoteFetch = requests.find(value => value.url === "https://i.scdn.co/image/runtime-cover.png");
    const upload = requests.find(value => value.url.endsWith("/ingest/v1/artwork"));
    assert.equal(stateRequests.length, 1);
    assert.equal("artworkUrl" in JSON.parse(stateRequests[0].data), false);
    assert.ok(remoteFetch, requests.map(value => value.url).join(", "));
    assert.ok(upload, requests.map(value => value.url).join(", "));
    assert.equal(remoteFetch.responseType, "arraybuffer");
    assert.equal(upload.data.byteLength, png.byteLength);
    assert.equal(upload.headers["Content-Type"], "image/png");
    assert.equal(
        upload.headers["X-NPO-Producer-Revision"],
        String(JSON.parse(stateRequests[0].data).producerRevision));

    now = 5500;
    tick();
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(
        requests.filter(value => value.url.endsWith("/ingest/v1/state")).length,
        1);
    assert.equal(
        requests.filter(value => value.url.endsWith("/ingest/v1/heartbeat")).length,
        1);
});

test("leader selection prefers playing then visible then recent activity", () => {
    const now = 10_000;
    const track = { title: "Track" };
    const candidates = [
        { tabId: "paused", observedAt: now, activeAt: 9_900, visible: true, state: { playback: "paused", track } },
        { tabId: "hidden", observedAt: now, activeAt: 9_800, visible: false, state: { playback: "playing", track } },
        { tabId: "visible", observedAt: now, activeAt: 9_700, visible: true, state: { playback: "playing", track } },
    ];
    assert.equal(api.selectLeader(candidates, now), "visible");
    assert.equal(
        api.selectLeader([{ ...candidates[2], observedAt: now - api.candidateLifetimeMs - 1 }], now),
        null);
});

test("a paused or stopped contender cannot steal an existing lease", () => {
    const now = 10_000;
    const track = { title: "Track" };
    const candidates = [
        {
            tabId: "owner",
            observedAt: now,
            activeAt: 8_000,
            visible: false,
            ownsLease: true,
            state: { playback: "paused", track },
        },
        {
            tabId: "contender",
            observedAt: now,
            activeAt: 9_900,
            visible: true,
            ownsLease: false,
            state: { playback: "stopped", track },
        },
    ];

    assert.equal(api.selectLeader(candidates, now), "owner");
    candidates[1].state.playback = "playing";
    assert.equal(api.selectLeader(candidates, now), "contender");
});

test("two playing tabs use visibility before prior lease ownership", () => {
    const now = 10_000;
    const track = { title: "Track" };
    const candidates = [
        {
            tabId: "hidden-owner",
            observedAt: now,
            activeAt: 9_900,
            visible: false,
            ownsLease: true,
            state: { playback: "playing", track },
        },
        {
            tabId: "visible-player",
            observedAt: now,
            activeAt: 9_800,
            visible: true,
            ownsLease: false,
            state: { playback: "playing", track },
        },
    ];

    assert.equal(api.selectLeader(candidates, now), "visible-player");
});
