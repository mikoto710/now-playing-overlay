import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const source = readFileSync(new URL("../NowPlayingOverlay.user.js", import.meta.url), "utf8");
const api = {};
vm.runInNewContext(source, { __NOW_PLAYING_OVERLAY_TEST__: api });

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
