import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const source = readFileSync(new URL("../NowPlayingOverlay.user.js", import.meta.url), "utf8");
const api = {};
vm.runInNewContext(source, { __NOW_PLAYING_OVERLAY_TEST__: api });

function element(text = null, options = {}) {
    const classes = new Set(options.classes ?? []);
    return {
        textContent: text,
        innerText: text,
        paused: options.paused ?? true,
        ended: options.ended ?? false,
        readyState: options.readyState ?? 1,
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

function mediaContext(document, playbackState = "playing") {
    return {
        document,
        mediaSession: {
            playbackState,
            metadata: {
                title: "Media Title",
                artist: "Media Artist",
                album: "Media Album",
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
        })),
        {
            playback: "playing",
            track: {
                title: "Track Title",
                artist: "Artist",
                albumTitle: "Album",
                trackId: null,
            },
        });
    assert.deepEqual(
        structuredClone(api.normalizeAdapterState({ playback: "playing" })),
        { playback: "idle", track: null });
});

test("Media Session sites preserve the page-provided title and fields", () => {
    const playingMedia = element(null, { paused: false });
    const document = documentWith({}, { "audio, video": [playingMedia] });
    const context = mediaContext(document);

    for (const hostname of ["open.spotify.com", "app.plex.tv"]) {
        assert.deepEqual(structuredClone(api.readSiteState(hostname, context)), {
            playback: "playing",
            title: "Media Title",
            artist: "Media Artist",
            albumTitle: "Media Album",
        });
    }

    const deezerDocument = documentWith(
        { '[data-testid="play_button_pause"]': element() },
        { "audio, video": [] });
    assert.equal(api.readSiteState("www.deezer.com", mediaContext(deezerDocument)).playback, "playing");

    const pretzelDocument = documentWith(
        { '[data-heapid="music-player"] [data-testid="play-button"]': element() },
        { "audio, video": [] });
    assert.equal(api.readSiteState("play.pretzel.rocks", mediaContext(pretzelDocument)).playback, "stopped");
});

test("SoundCloud and Yandex adapters read their current player elements", () => {
    const soundCloud = documentWith({
        ".playControl": element(null, { classes: ["playing"] }),
        ".playbackSoundBadge__titleLink": element(null, { attributes: { title: "Cloud Track" } }),
        ".playbackSoundBadge__lightLink": element(null, { attributes: { title: "Cloud Artist" } }),
        "div.playlist.playing .soundTitle__title": element("Cloud Album"),
    });
    assert.deepEqual(structuredClone(api.readSiteState("soundcloud.com", { document: soundCloud })), {
        playback: "playing",
        title: "Cloud Track",
        artist: "Cloud Artist",
        albumTitle: "Cloud Album",
    });

    const yandex = documentWith({
        '[class*="VibePlayerControls_playButton"]': element(null, { classes: ["button_playing"] }),
        '[class*="VibePlayerbarMeta_trackNameText"]': element("Yandex Track"),
        '[class*="VibePage_text"]': element("Yandex Artist"),
    });
    assert.deepEqual(structuredClone(api.readSiteState("music.yandex.com", { document: yandex })), {
        playback: "playing",
        title: "Yandex Track",
        artist: "Yandex Artist",
    });
});

test("YouTube adapters use separate artist data instead of guessing title order", () => {
    const youtube = documentWith({
        "#text > a": element("Video Artist"),
        "#container > h1 > yt-formatted-string": element("Video Artist - Video Track (Official Audio)"),
        video: element(null, { paused: false }),
    });
    assert.deepEqual(structuredClone(api.readSiteState("www.youtube.com", { document: youtube })), {
        playback: "playing",
        title: "Video Track",
        artist: "Video Artist",
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
    const context = mediaContext(youtubeMusic, "paused");
    assert.deepEqual(structuredClone(api.readSiteState("music.youtube.com", context)), {
        playback: "paused",
        title: "Music Track",
        artist: "Artist One, Artist Two",
        albumTitle: "Music Album",
    });
});

test("Bilibili and Chillhop adapters retain their site-specific fallbacks", () => {
    const bilibili = documentWith(
        {
            "a.up-name": element("Uploader"),
            video: element(null, { paused: false }),
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
    });

    const chillhop = documentWith({
        "#p-btn-play": element(null, { classes: ["playing"] }),
        ".p-track-title": element("Chill Track"),
        ".p-track-artist": element("Chill Artist"),
    });
    assert.deepEqual(structuredClone(api.readSiteState("chillhop.com", { document: chillhop })), {
        playback: "playing",
        title: "Chill Track",
        artist: "Chill Artist",
    });
    assert.equal(api.readSiteState("unsupported.example", { document: chillhop }), null);
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
