// ==UserScript==
// @name         Now Playing Overlay Browser Producer
// @namespace    https://github.com/mikoto710/now-playing-overlay
// @version      0.2.0
// @description  Send browser playback metadata to the local Now Playing Overlay host.
// @author       Now Playing Overlay contributors
// @license      GPL-3.0-or-later
// @match        https://open.spotify.com/*
// @match        https://music.youtube.com/*
// @match        https://www.youtube.com/*
// @match        https://soundcloud.com/*
// @match        https://www.deezer.com/*
// @match        https://music.yandex.com/*
// @match        https://music.yandex.ru/*
// @match        https://play.pretzel.rocks/*
// @match        https://app.plex.tv/*
// @match        https://chillhop.com/*
// @match        https://www.bilibili.com/*
// @connect      127.0.0.1
// @grant        GM_getValue
// @grant        GM_setValue
// @grant        GM_deleteValue
// @grant        GM_listValues
// @grant        GM_registerMenuCommand
// @grant        GM_xmlhttpRequest
// @grant        GM.xmlHttpRequest
// @run-at       document-start
// @noframes
// ==/UserScript==

(() => {
    "use strict";

    const connectionStorageKey = "connectionCode";
    const producerStorageKey = "producerId";
    const revisionStorageKey = "producerRevision";
    const candidatePrefix = "candidate:";
    const candidateLifetimeMs = 3000;
    const startupElectionDelayMs = 1000;
    const sampleIntervalMs = 500;
    const heartbeatIntervalMs = 3000;
    const stateRefreshIntervalMs = 5000;
    const retryIntervalMs = 1500;

    function parseConnectionCode(value) {
        if (typeof value !== "string") {
            return null;
        }

        const match = /^npo1:([1-9][0-9]{0,4}):([A-Za-z0-9_-]{43})$/.exec(value.trim());
        if (!match) {
            return null;
        }

        const port = Number(match[1]);
        return port <= 65535 ? { port, key: match[2] } : null;
    }

    function cleanText(value) {
        if (typeof value !== "string") {
            return null;
        }

        const normalized = value.replace(/[\u0000-\u001f\u007f-\u009f\u2028\u2029]+/g, " ")
            .replace(/\s+/g, " ")
            .trim();
        return normalized || null;
    }

    function normalizeAdapterState(value) {
        if (!value || typeof value !== "object") {
            return { playback: "idle", track: null };
        }

        const title = cleanText(value.title);
        const playback = ["playing", "paused", "stopped"].includes(value.playback)
            ? value.playback
            : title
                ? "stopped"
                : "idle";
        if (!title) {
            return { playback: playback === "playing" ? "idle" : playback, track: null };
        }

        return {
            playback,
            track: {
                title,
                artist: cleanText(value.artist),
                albumTitle: cleanText(value.albumTitle),
                trackId: cleanText(value.trackId),
            },
        };
    }

    function firstElement(root, selectors) {
        for (const selector of selectors) {
            const element = root.querySelector(selector);
            if (element) {
                return element;
            }
        }
        return null;
    }

    function elementText(element, attributes = []) {
        if (!element) {
            return null;
        }

        for (const attribute of attributes) {
            const value = typeof element.getAttribute === "function"
                ? element.getAttribute(attribute)
                : element[attribute];
            const normalized = cleanText(value);
            if (normalized) {
                return normalized;
            }
        }
        return cleanText(element.textContent ?? element.innerText);
    }

    function joinedElementText(elements) {
        return [...new Set(Array.from(elements, element => elementText(element)).filter(Boolean))].join(", ") || null;
    }

    function readPlayback(documentRoot, mediaSession) {
        const media = Array.from(documentRoot.querySelectorAll("audio, video"));
        const activeMedia = media.find(element => !element.paused && !element.ended);
        const pausedMedia = media.find(element => element.paused && !element.ended && element.readyState > 0);
        const sessionState = mediaSession?.playbackState;
        return sessionState === "playing" || activeMedia
            ? "playing"
            : sessionState === "paused" || pausedMedia
                ? "paused"
                : "stopped";
    }

    function readMediaSessionState(context, playback = null) {
        const metadata = context.mediaSession?.metadata;
        if (!cleanText(metadata?.title)) {
            return null;
        }

        return {
            playback: playback ?? readPlayback(context.document, context.mediaSession),
            title: metadata.title,
            artist: metadata.artist,
            albumTitle: metadata.album,
        };
    }

    function readSoundCloud(context) {
        const title = elementText(
            context.document.querySelector(".playbackSoundBadge__titleLink"),
            ["title"]);
        if (!title) {
            return null;
        }

        const playControl = context.document.querySelector(".playControl");
        return {
            playback: playControl?.classList?.contains("playing") ? "playing" : "stopped",
            title,
            artist: elementText(
                context.document.querySelector(".playbackSoundBadge__lightLink"),
                ["title"]),
            albumTitle: elementText(firstElement(context.document, [
                "div.playlist.playing .soundTitle__title",
                ".fullListenHero .soundTitle__title",
            ])),
        };
    }

    function readYandexMusic(context) {
        const title = elementText(
            context.document.querySelector('[class*="VibePlayerbarMeta_trackNameText"]'));
        if (!title) {
            return null;
        }

        const playButton = context.document.querySelector('[class*="VibePlayerControls_playButton"]');
        const playing = Array.from(playButton?.classList ?? []).some(name => name.includes("_playing"));
        return {
            playback: playing ? "playing" : "stopped",
            title,
            artist: elementText(context.document.querySelector('[class*="VibePage_text"]')),
        };
    }

    function cleanYouTubeTitle(title, artist) {
        let result = cleanText(title);
        if (!result) {
            return null;
        }

        if (artist) {
            for (const decoration of [`${artist} - `, ` - ${artist}`, artist]) {
                result = result.replace(decoration, "").trim();
            }
        }
        for (const decoration of [
            "(Official Audio)",
            "(Official Music Video)",
            "(Original Video)",
            "(Original Mix)",
        ]) {
            result = result.replace(decoration, "").trim();
        }
        return result || null;
    }

    function readYouTube(context) {
        const artist = elementText(firstElement(context.document, [
            "#text > a",
            "ytd-video-owner-renderer #channel-name a",
            "#owner #channel-name a",
        ]));
        const title = cleanYouTubeTitle(elementText(firstElement(context.document, [
            "#container > h1 > yt-formatted-string",
            "h1.ytd-watch-metadata yt-formatted-string",
            "h1 yt-formatted-string",
        ])), artist);
        if (!title) {
            return null;
        }

        const media = context.document.querySelector("video") ?? context.document.querySelector("audio");
        return {
            playback: media ? (media.paused || media.ended ? "stopped" : "playing") : "stopped",
            title,
            artist,
        };
    }

    function readYouTubeMusic(context) {
        const title = elementText(
            context.document.querySelector(".ytmusic-player-bar.title"),
            ["title"]);
        if (!title) {
            return null;
        }

        const artists = joinedElementText(context.document.querySelectorAll([
            '.ytmusic-player-bar.byline [href*="channel/"]:not([href*="channel/MPREb_"]):not([href*="browse/MPREb_"])',
            '.ytmusic-player-bar.byline .yt-formatted-string:nth-child(2n+1):not([href*="browse/"]):not([href*="channel/"]):not(:nth-last-child(1)):not(:nth-last-child(3))',
            '.ytmusic-player-bar.byline [href*="browse/FEmusic_library_privately_owned_artist_detaila_"]',
        ].join(", ")));
        const album = elementText(firstElement(context.document, [
            '.ytmusic-player-bar [href*="browse/MPREb_"]',
            '.ytmusic-player-bar [href*="browse/FEmusic_library_privately_owned_release_detailb_"]',
        ]));
        return {
            playback: readPlayback(context.document, context.mediaSession),
            title,
            artist: artists,
            albumTitle: album,
        };
    }

    function readDeezer(context) {
        const pauseButton = context.document.querySelector('[data-testid="play_button_pause"]');
        const playButton = context.document.querySelector('[data-testid="play_button_play"]');
        const playback = pauseButton ? "playing" : playButton ? "paused" : null;
        return readMediaSessionState(context, playback);
    }

    function readPretzel(context) {
        const pauseButton = context.document.querySelector('[data-heapid="music-player"] [data-testid="pause-button"]');
        const playButton = context.document.querySelector('[data-heapid="music-player"] [data-testid="play-button"]');
        const playback = pauseButton ? "playing" : playButton ? "stopped" : null;
        return readMediaSessionState(context, playback);
    }

    function readBilibili(context) {
        const titleElement = context.document.querySelector("h1.video-title");
        const title = elementText(titleElement, ["data-title", "title"])
            ?? cleanText(context.document.title);
        if (!title) {
            return null;
        }

        const primaryArtist = elementText(context.document.querySelector("a.up-name"));
        const artist = primaryArtist
            ?? joinedElementText(context.document.querySelectorAll("a.staff-name"));
        const match = /\/video\/(BV[0-9A-Za-z]+)/.exec(context.locationHref ?? "")
            ?? /[?&]bvid=(BV[0-9A-Za-z]+)/.exec(context.locationHref ?? "");
        return {
            playback: readPlayback(context.document, context.mediaSession),
            title,
            artist,
            trackId: match?.[1] ?? null,
        };
    }

    function readChillhop(context) {
        const title = elementText(context.document.querySelector(".p-track-title"));
        if (!title) {
            return null;
        }

        return {
            playback: context.document.querySelector("#p-btn-play")?.classList?.contains("playing")
                ? "playing"
                : "stopped",
            title,
            artist: elementText(context.document.querySelector(".p-track-artist")),
        };
    }

    const siteReaders = {
        "open.spotify.com": readMediaSessionState,
        "soundcloud.com": readSoundCloud,
        "music.yandex.com": readYandexMusic,
        "music.yandex.ru": readYandexMusic,
        "www.deezer.com": readDeezer,
        "play.pretzel.rocks": readPretzel,
        "www.youtube.com": readYouTube,
        "music.youtube.com": readYouTubeMusic,
        "app.plex.tv": readMediaSessionState,
        "www.bilibili.com": readBilibili,
        "chillhop.com": readChillhop,
    };

    function readSiteState(hostname, context) {
        return Object.hasOwn(siteReaders, hostname)
            ? siteReaders[hostname](context)
            : null;
    }

    function selectLeader(candidates, now) {
        const eligible = candidates.filter(candidate =>
            candidate
            && typeof candidate.tabId === "string"
            && Number.isFinite(candidate.observedAt)
            && now - candidate.observedAt <= candidateLifetimeMs
            && candidate.state?.track);
        eligible.sort((left, right) => {
            const leftPlaying = left.state.playback === "playing";
            const rightPlaying = right.state.playback === "playing";
            const playingDifference = Number(rightPlaying) - Number(leftPlaying);
            if (playingDifference !== 0) {
                return playingDifference;
            }

            // Once playback is no longer active, retain the lease owner until it disappears.
            // This prevents an unrelated paused or stopped tab from stealing the overlay.
            if (!leftPlaying && !rightPlaying) {
                const ownershipDifference = Number(Boolean(right.ownsLease))
                    - Number(Boolean(left.ownsLease));
                if (ownershipDifference !== 0) {
                    return ownershipDifference;
                }
            }

            const visibilityDifference = Number(Boolean(right.visible)) - Number(Boolean(left.visible));
            if (visibilityDifference !== 0) {
                return visibilityDifference;
            }

            const activityDifference = (right.activeAt || 0) - (left.activeAt || 0);
            return activityDifference || left.tabId.localeCompare(right.tabId);
        });
        return eligible[0]?.tabId ?? null;
    }

    const testApi = globalThis.__NOW_PLAYING_OVERLAY_TEST__;
    if (testApi && typeof testApi === "object") {
        Object.assign(testApi, {
            parseConnectionCode,
            cleanText,
            normalizeAdapterState,
            cleanYouTubeTitle,
            readSiteState,
            readMediaSessionState,
            selectLeader,
            candidateLifetimeMs,
        });
        return;
    }

    const tabId = crypto.randomUUID();
    const candidateStorageKey = `${candidatePrefix}${tabId}`;
    const startedAt = Date.now();
    const observedMediaElements = new WeakSet();
    let producerId = readOrCreateProducerId();
    let lastPlayback = "idle";
    let lastStateFingerprint = null;
    let lastStateSentAt = 0;
    let lastHeartbeatSentAt = 0;
    let lastAttemptAt = 0;
    let lastActiveAt = Date.now();
    let requestPending = false;
    let leaseClaimed = false;
    let stopped = false;

    const adapters = [
        {
            id: "site",
            matches: () => Object.hasOwn(siteReaders, window.location.hostname),
            read: () => readSiteState(window.location.hostname, currentAdapterContext()),
        },
        {
            id: "media-session",
            matches: () => "mediaSession" in navigator,
            read: () => readMediaSessionState(currentAdapterContext()),
        },
    ];

    function currentAdapterContext() {
        return {
            document,
            mediaSession: navigator.mediaSession,
            locationHref: window.location.href,
        };
    }

    GM_registerMenuCommand("Configure Now Playing Overlay", configureConnection);
    GM_registerMenuCommand("Show Now Playing Overlay status", showStatus);
    GM_registerMenuCommand("Clear Now Playing Overlay connection", clearConnection);

    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible") {
            lastActiveAt = Date.now();
        }
    });
    window.addEventListener("pagehide", stop, { once: true });
    window.addEventListener("beforeunload", stop, { once: true });
    const timer = window.setInterval(tick, sampleIntervalMs);
    tick();

    function readOrCreateProducerId() {
        const existing = GM_getValue(producerStorageKey, null);
        if (typeof existing === "string"
            && /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(existing)) {
            return existing;
        }

        const created = crypto.randomUUID();
        GM_setValue(producerStorageKey, created);
        return created;
    }

    function configureConnection() {
        const current = GM_getValue(connectionStorageKey, "");
        const entered = window.prompt(
            "Paste the connection code copied from Now Playing Overlay Settings:",
            current);
        if (entered === null) {
            return;
        }

        const parsed = parseConnectionCode(entered);
        if (!parsed) {
            window.alert("The connection code is invalid. Copy it again from Now Playing Overlay Settings.");
            return;
        }

        GM_setValue(connectionStorageKey, entered.trim());
        resetTransport();
        window.alert("Now Playing Overlay is configured. Start playback on this page.");
    }

    function showStatus() {
        const connection = parseConnectionCode(GM_getValue(connectionStorageKey, ""));
        const message = connection
            ? `Configured for 127.0.0.1:${connection.port}. ${leaseClaimed ? "Connected." : "Waiting for active media or the local host."}`
            : "Not configured. Use Configure Now Playing Overlay and paste the code from the app.";
        window.alert(message);
    }

    function clearConnection() {
        GM_deleteValue(connectionStorageKey);
        resetTransport();
        window.alert("The Now Playing Overlay connection was cleared.");
    }

    function resetTransport() {
        leaseClaimed = false;
        requestPending = false;
        lastStateFingerprint = null;
        lastStateSentAt = 0;
        lastHeartbeatSentAt = 0;
        lastAttemptAt = 0;
    }

    function tick() {
        if (stopped) {
            return;
        }

        const now = Date.now();
        const state = readCurrentState();
        if (state.playback === "playing" && lastPlayback !== "playing") {
            lastActiveAt = now;
        }
        lastPlayback = state.playback;

        const candidate = {
            tabId,
            observedAt: now,
            activeAt: lastActiveAt,
            visible: document.visibilityState === "visible",
            ownsLease: leaseClaimed,
            state,
        };
        GM_setValue(candidateStorageKey, candidate);
        const candidates = readCandidates(now);
        const leader = selectLeader(candidates, now);
        if (leader !== tabId || now - startedAt < startupElectionDelayMs) {
            leaseClaimed = false;
            return;
        }

        const connection = parseConnectionCode(GM_getValue(connectionStorageKey, ""));
        if (!connection || requestPending || now - lastAttemptAt < retryIntervalMs) {
            return;
        }

        const fingerprint = JSON.stringify(state);
        if (!leaseClaimed
            || fingerprint !== lastStateFingerprint
            || now - lastStateSentAt >= stateRefreshIntervalMs) {
            sendState(connection, state, fingerprint, now);
        }
        else if (now - lastHeartbeatSentAt >= heartbeatIntervalMs) {
            sendHeartbeat(connection, now);
        }
    }

    function readCandidates(now) {
        const candidates = [];
        for (const key of GM_listValues()) {
            if (!key.startsWith(candidatePrefix)) {
                continue;
            }

            const candidate = GM_getValue(key, null);
            if (!candidate
                || !Number.isFinite(candidate.observedAt)
                || now - candidate.observedAt > candidateLifetimeMs) {
                GM_deleteValue(key);
                continue;
            }

            candidates.push(candidate);
        }
        return candidates;
    }

    function readCurrentState() {
        observeMediaElements();
        for (const adapter of adapters) {
            try {
                if (adapter.matches()) {
                    const value = adapter.read();
                    if (value) {
                        return normalizeAdapterState(value);
                    }
                }
            }
            catch (error) {
                console.debug(`[Now Playing Overlay] ${adapter.id} adapter could not read metadata.`, error);
            }
        }
        return { playback: "idle", track: null };
    }

    function observeMediaElements() {
        for (const element of document.querySelectorAll("audio, video")) {
            if (observedMediaElements.has(element)) {
                continue;
            }

            observedMediaElements.add(element);
            for (const eventName of ["play", "playing", "pause", "ended", "loadedmetadata"]) {
                element.addEventListener(eventName, () => {
                    lastActiveAt = Date.now();
                }, { passive: true });
            }
        }
    }

    function nextRevision() {
        const stored = Number(GM_getValue(revisionStorageKey, 0));
        const revision = Math.max(Date.now(), Number.isSafeInteger(stored) ? stored + 1 : 1);
        GM_setValue(revisionStorageKey, revision);
        return revision;
    }

    function sendState(connection, state, fingerprint, now) {
        const payload = {
            producerId,
            producerRevision: nextRevision(),
            playback: state.playback,
            track: state.track,
        };
        send(connection, "/ingest/v1/state", payload, now, status => {
            if (status === 204) {
                leaseClaimed = true;
                lastStateFingerprint = fingerprint;
                lastStateSentAt = Date.now();
                lastHeartbeatSentAt = Date.now();
            }
            else {
                leaseClaimed = false;
            }
        });
    }

    function sendHeartbeat(connection, now) {
        send(connection, "/ingest/v1/heartbeat", { producerId }, now, status => {
            if (status === 204) {
                leaseClaimed = true;
                lastHeartbeatSentAt = Date.now();
            }
            else {
                leaseClaimed = false;
            }
        });
    }

    function send(connection, path, payload, now, complete) {
        requestPending = true;
        lastAttemptAt = now;
        makeRequest({
            method: "POST",
            url: `http://127.0.0.1:${connection.port}${path}`,
            data: JSON.stringify(payload),
            timeout: 5000,
            headers: {
                "Authorization": `Bearer ${connection.key}`,
                "Content-Type": "application/json; charset=utf-8",
            },
            onload: response => {
                requestPending = false;
                complete(response.status);
                if (response.status === 401) {
                    console.warn("[Now Playing Overlay] The connection code was rejected. Copy a new code from Settings.");
                }
            },
            onerror: () => {
                requestPending = false;
                leaseClaimed = false;
            },
            ontimeout: () => {
                requestPending = false;
                leaseClaimed = false;
            },
        });
    }

    function makeRequest(options) {
        if (typeof GM_xmlhttpRequest === "function") {
            return GM_xmlhttpRequest(options);
        }
        if (globalThis.GM && typeof globalThis.GM.xmlHttpRequest === "function") {
            return globalThis.GM.xmlHttpRequest(options);
        }
        throw new Error("Tampermonkey GM_xmlhttpRequest is unavailable.");
    }

    function stop() {
        if (stopped) {
            return;
        }
        stopped = true;
        window.clearInterval(timer);
        GM_deleteValue(candidateStorageKey);
    }
})();
