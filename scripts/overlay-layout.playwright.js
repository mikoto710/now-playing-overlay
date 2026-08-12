/* playwright-cli --filename loads this entire file as a function expression; keep the final
 * closing brace without a trailing semicolon. */
async (page) => {
  const bootstrapUrl = page.url();
  const fragmentStart = bootstrapUrl.indexOf("#");
  const overlayUrl =
    fragmentStart === -1
      ? ""
      : decodeURIComponent(bootstrapUrl.slice(fragmentStart + 1));
  if (!/^http:\/\/127\.0\.0\.1:\d+\/NowPlaying\.html$/.test(overlayUrl)) {
    throw new Error(
      "The bootstrap page is missing a valid loopback overlay URL.",
    );
  }

  const instanceId = "f64b0c0f-73f3-4c0c-8b76-e84b89b77db2";
  const artworkIds = {
    initial: "a".repeat(64),
    delayed: "b".repeat(64),
    final: "c".repeat(64),
  };
  const delayedArtworkMs = 800;

  const createSnapshot = ({
    revision,
    playback = "playing",
    artist,
    title,
    artworkId = artworkIds.initial,
  }) => ({
    protocolVersion: 2,
    serverInstanceId: instanceId,
    snapshotRevision: revision,
    source: { provider: "windows-media" },
    playback,
    track: {
      title,
      artist,
      albumTitle: "OV2-03 Layout Fixture",
      albumArtist: null,
      subtitle: null,
      trackNumber: 1,
      albumTrackCount: 1,
      playbackType: "music",
      genres: [],
    },
    artwork: {
      artworkRevision: revision,
      artworkId,
      url: `/api/v2/artwork/${artworkId}`,
    },
    observedAt: "2026-08-12T00:00:00Z",
  });

  const initialState = createSnapshot({
    revision: 1,
    artist: "Artist A",
    title: "Track A",
  });

  await page.addInitScript(() => {
    let currentEventSource;

    class ControlledEventSource {
      constructor() {
        this.onerror = null;
        this.listeners = new Map();
        currentEventSource = this;
      }

      addEventListener(type, listener) {
        const listeners = this.listeners.get(type) ?? [];
        listeners.push(listener);
        this.listeners.set(type, listeners);
      }

      close() {}

      emit(type, value) {
        for (const listener of this.listeners.get(type) ?? []) {
          listener({ data: JSON.stringify(value) });
        }
      }
    }

    Object.defineProperty(window, "EventSource", {
      configurable: true,
      value: ControlledEventSource,
    });
    window.__emitOverlayState = (snapshot) => {
      if (!currentEventSource) {
        throw new Error("Overlay EventSource has not started.");
      }
      currentEventSource.emit("state", snapshot);
    };
  });

  await page.route("**/api/v2/state", (route) =>
    route.fulfill({
      body: JSON.stringify(initialState),
      contentType: "application/json",
      status: 200,
    }),
  );
  await page.route("**/api/v2/appearance", (route) =>
    route.fulfill({
      body: JSON.stringify({
        appearanceVersion: 1,
        preset: "default",
        artistColor: "#25C7A0",
        trackColor: "#FFFFFF",
        backgroundColor: "#1B1D20",
        backgroundOpacityPercent: 100,
        cornerRadius: 0,
      }),
      contentType: "application/json",
      status: 200,
    }),
  );
  await page.route("**/api/v2/artwork/**", async (route) => {
    const artworkId = route.request().url().split("/").at(-1);
    if (artworkId === artworkIds.delayed) {
      await page.waitForTimeout(delayedArtworkMs);
    }

    const fill = artworkId === artworkIds.final ? "#25c7a0" : "#1b1d20";
    await route.fulfill({
      body: `<svg xmlns="http://www.w3.org/2000/svg" width="2" height="2"><rect width="2" height="2" fill="${fill}"/></svg>`,
      contentType: "image/svg+xml",
      status: 200,
    });
  });

  await page.setViewportSize({ width: 350, height: 70 });
  await page.goto(overlayUrl, { waitUntil: "networkidle" });
  await page.waitForFunction(
    ({ artist, title, artworkId }) => {
      const artwork = document.getElementById("artwork-base");
      const root = document.getElementById("now-playing");
      return (
        document.getElementById("artist-text")?.textContent === artist &&
        document.getElementById("track-text")?.textContent === title &&
        artwork instanceof HTMLImageElement &&
        !artwork.hidden &&
        artwork.src.endsWith(artworkId) &&
        root?.style.transform === "translateX(0px)"
      );
    },
    {
      artist: initialState.track.artist,
      title: initialState.track.title,
      artworkId: artworkIds.initial,
    },
  );

  const assert = (condition, message) => {
    if (!condition) {
      throw new Error(message);
    }
  };
  const assertClose = (actual, expected, message, tolerance = 0.25) => {
    assert(
      Math.abs(actual - expected) <= tolerance,
      `${message}: expected ${expected}, received ${actual}`,
    );
  };
  const isTransparent = (color) =>
    color === "rgba(0, 0, 0, 0)" || color === "transparent";
  const readLayout = () =>
    page.evaluate(() => {
      const readBounds = (id) => {
        const bounds = document.getElementById(id).getBoundingClientRect();
        return {
          bottom: bounds.bottom,
          height: bounds.height,
          left: bounds.left,
          right: bounds.right,
          top: bounds.top,
          width: bounds.width,
        };
      };
      const artist = document.getElementById("artist");
      const track = document.getElementById("track");
      const artwork = document.getElementById("artwork-base");
      const root = document.getElementById("now-playing");
      const stage = document.getElementById("overlay-stage");
      const artistStyle = getComputedStyle(artist);
      const trackStyle = getComputedStyle(track);
      const artworkStyle = getComputedStyle(artwork);
      const rootStyle = getComputedStyle(root);
      return {
        artwork: readBounds("artwork-base"),
        artworkObjectFit: artworkStyle.objectFit,
        artist: readBounds("artist"),
        artistColor: artistStyle.color,
        artistFontSize: artistStyle.fontSize,
        artistFontWeight: artistStyle.fontWeight,
        artistLineHeight: artistStyle.lineHeight,
        bodyBackground: getComputedStyle(document.body).backgroundColor,
        details: readBounds("track-details"),
        htmlBackground: getComputedStyle(document.documentElement)
          .backgroundColor,
        root: readBounds("now-playing"),
        rootBackground: rootStyle.backgroundColor,
        rootBorderRadius: rootStyle.borderRadius,
        rootOverflow: rootStyle.overflow,
        scale: Number.parseFloat(
          stage.style.getPropertyValue("--overlay-scale"),
        ),
        stage: readBounds("overlay-stage"),
        track: readBounds("track"),
        trackColor: trackStyle.color,
        trackFontSize: trackStyle.fontSize,
        trackFontWeight: trackStyle.fontWeight,
        trackLineHeight: trackStyle.lineHeight,
        viewport: { height: window.innerHeight, width: window.innerWidth },
      };
    });

  const defaultLayout = await readLayout();
  assert(
    defaultLayout.artistColor === "rgb(37, 199, 160)",
    "Default artist color changed.",
  );
  assert(
    defaultLayout.trackColor === "rgb(255, 255, 255)",
    "Default track color changed.",
  );
  assert(
    defaultLayout.artistFontSize === "16px",
    "Default artist font size changed.",
  );
  assert(
    defaultLayout.trackFontSize === "22px",
    "Default track font size changed.",
  );
  assert(
    defaultLayout.artistFontWeight === "600",
    "Default artist font weight changed.",
  );
  assert(
    defaultLayout.artistLineHeight === "19px",
    "Default artist line height changed.",
  );
  assert(
    defaultLayout.trackFontWeight === "700",
    "Default track font weight changed.",
  );
  assert(
    defaultLayout.trackLineHeight === "26px",
    "Default track line height changed.",
  );
  assert(
    defaultLayout.artist.top < defaultLayout.track.top,
    "Artist must remain above track.",
  );
  assert(
    defaultLayout.rootBackground === "rgb(27, 29, 32)",
    "Default overlay background changed.",
  );
  assert(
    defaultLayout.rootBorderRadius === "0px",
    "Default overlay corner radius changed.",
  );
  assert(
    defaultLayout.rootOverflow === "hidden",
    "Logical canvas must remain clipped.",
  );
  assert(
    defaultLayout.artworkObjectFit === "contain",
    "Artwork must retain contain composition.",
  );

  await page.evaluate(() => {
    const style = document.documentElement.style;
    style.setProperty("--overlay-artist-color", "#123456");
    style.setProperty("--overlay-track-color", "#ABCDEF");
    style.setProperty("--overlay-background", "rgba(16, 32, 48, 0.65)");
    style.setProperty("--overlay-corner-radius", "12px");
  });
  const customLayout = await readLayout();
  assert(customLayout.artistColor === "rgb(18, 52, 86)", "Custom artist color was not applied.");
  assert(customLayout.trackColor === "rgb(171, 205, 239)", "Custom track color was not applied.");
  assert(
    customLayout.rootBackground === "rgba(16, 32, 48, 0.65)",
    "Custom background color and opacity were not applied.",
  );
  assert(customLayout.rootBorderRadius === "12px", "Custom corner radius was not applied.");
  assert(
    !(await page
      .locator("#artist")
      .evaluate((element) => element.classList.contains("is-marquee"))),
    "Short artist text unexpectedly entered marquee mode.",
  );
  assert(
    !(await page
      .locator("#track")
      .evaluate((element) => element.classList.contains("is-marquee"))),
    "Short track text unexpectedly entered marquee mode.",
  );

  const viewports = [
    { height: 70, name: "350x70", scale: 1, width: 350 },
    { height: 140, name: "700x140", scale: 2, width: 700 },
    { height: 210, name: "1050x210", scale: 3, width: 1050 },
    { height: 280, name: "1400x280", scale: 4, width: 1400 },
    { height: 350, name: "1750x350", scale: 5, width: 1750 },
    { height: 140, name: "900x140-height-limited", scale: 2, width: 900 },
    { height: 220, name: "700x220-width-limited", scale: 2, width: 700 },
  ];
  const layoutResults = [];

  for (const viewport of viewports) {
    await page.setViewportSize({
      width: viewport.width,
      height: viewport.height,
    });
    await page.waitForFunction(
      (expectedScale) =>
        Number.parseFloat(
          document
            .getElementById("overlay-stage")
            .style.getPropertyValue("--overlay-scale"),
        ) === expectedScale,
      viewport.scale,
    );
    const layout = await readLayout();
    const expectedWidth = 350 * viewport.scale;
    const expectedHeight = 70 * viewport.scale;
    const expectedLeft = (viewport.width - expectedWidth) / 2;
    const expectedTop = (viewport.height - expectedHeight) / 2;

    assertClose(layout.scale, viewport.scale, `${viewport.name} CSS scale`);
    assertClose(
      layout.stage.width,
      expectedWidth,
      `${viewport.name} stage width`,
    );
    assertClose(
      layout.stage.height,
      expectedHeight,
      `${viewport.name} stage height`,
    );
    assertClose(
      layout.stage.left,
      expectedLeft,
      `${viewport.name} left gutter`,
    );
    assertClose(layout.stage.top, expectedTop, `${viewport.name} top gutter`);
    assertClose(
      layout.root.left,
      layout.stage.left,
      `${viewport.name} root left`,
    );
    assertClose(layout.root.top, layout.stage.top, `${viewport.name} root top`);
    assertClose(
      layout.root.width,
      layout.stage.width,
      `${viewport.name} root width`,
    );
    assertClose(
      layout.root.height,
      layout.stage.height,
      `${viewport.name} root height`,
    );
    assertClose(
      layout.artwork.width,
      70 * viewport.scale,
      `${viewport.name} artwork width`,
    );
    assertClose(
      layout.artwork.height,
      70 * viewport.scale,
      `${viewport.name} artwork height`,
    );
    assertClose(
      layout.details.left,
      layout.stage.left + 70 * viewport.scale,
      `${viewport.name} details left`,
    );
    assertClose(
      layout.details.top,
      layout.stage.top + 7 * viewport.scale,
      `${viewport.name} details top`,
    );
    assertClose(
      layout.details.width,
      280 * viewport.scale,
      `${viewport.name} details width`,
    );
    assertClose(
      layout.details.height,
      62 * viewport.scale,
      `${viewport.name} details height`,
    );
    assert(
      layout.stage.left >= 0 && layout.stage.top >= 0,
      `${viewport.name} stage starts outside viewport.`,
    );
    assert(
      layout.stage.right <= viewport.width &&
        layout.stage.bottom <= viewport.height,
      `${viewport.name} stage is cropped.`,
    );
    assert(
      isTransparent(layout.bodyBackground),
      `${viewport.name} body gutter is not transparent.`,
    );
    assert(
      isTransparent(layout.htmlBackground),
      `${viewport.name} html gutter is not transparent.`,
    );

    layoutResults.push({
      gutters: {
        bottom: viewport.height - layout.stage.bottom,
        left: layout.stage.left,
        right: viewport.width - layout.stage.right,
        top: layout.stage.top,
      },
      name: viewport.name,
      scale: layout.scale,
      stage: { height: layout.stage.height, width: layout.stage.width },
    });
  }

  const longState = createSnapshot({
    revision: 2,
    artist: "Long Artist Name ".repeat(14),
    title: "Long Track Title ".repeat(18),
  });
  await page.evaluate(
    (snapshot) => window.__emitOverlayState(snapshot),
    longState,
  );
  await page.waitForFunction(
    ({ artist, title }) => {
      const artistElement = document.getElementById("artist");
      const trackElement = document.getElementById("track");
      return (
        document.getElementById("artist-text")?.textContent === artist &&
        document.getElementById("track-text")?.textContent === title &&
        artistElement?.classList.contains("is-marquee") &&
        trackElement?.classList.contains("is-marquee")
      );
    },
    { artist: longState.track.artist, title: longState.track.title },
  );
  await page.waitForFunction(() =>
    document
      .getElementById("artist")
      ?.getAnimations()
      .some(
        (animation) => animation.startTime !== null && Number(animation.currentTime ?? 0) > 0,
      ),
  );
  const marqueeBeforeResize = await page
    .locator("#artist")
    .evaluate((element) => {
      const animations = element.getAnimations();
      return {
        animationStart: Math.min(
          ...animations.map((animation) => animation.startTime ?? Infinity),
        ),
        copy: document.getElementById("artist-copy")?.textContent,
        currentTime: Math.max(
          ...animations.map((animation) => Number(animation.currentTime ?? 0)),
        ),
        gap: element.style.getPropertyValue("--marquee-gap"),
      };
    });
  assert(
    marqueeBeforeResize.copy === longState.track.artist,
    "Marquee copy must match artist text.",
  );
  assert(marqueeBeforeResize.gap === "40px", "Logical marquee gap changed.");
  assert(
    Number.isFinite(marqueeBeforeResize.animationStart),
    "Marquee animation did not start.",
  );

  await page.setViewportSize({ width: 1050, height: 210 });
  await page.waitForFunction(
    () =>
      Number.parseFloat(
        document
          .getElementById("overlay-stage")
          .style.getPropertyValue("--overlay-scale"),
      ) === 3,
  );
  await page.waitForTimeout(100);
  const marqueeAfterResize = await page
    .locator("#artist")
    .evaluate((element) => {
      const animations = element.getAnimations();
      return {
        animationStart: Math.min(
          ...animations.map((animation) => animation.startTime ?? Infinity),
        ),
        copy: document.getElementById("artist-copy")?.textContent,
        currentTime: Math.max(
          ...animations.map((animation) => Number(animation.currentTime ?? 0)),
        ),
        isMarquee: element.classList.contains("is-marquee"),
      };
    });
  assert(marqueeAfterResize.isMarquee, "Resize stopped marquee mode.");
  assert(
    marqueeAfterResize.copy === longState.track.artist,
    "Resize changed marquee content.",
  );
  assertClose(
    marqueeAfterResize.animationStart,
    marqueeBeforeResize.animationStart,
    "Resize restarted marquee animation",
    1,
  );
  assert(
    marqueeAfterResize.currentTime > marqueeBeforeResize.currentTime,
    "Marquee animation did not continue across resize.",
  );

  const pausedState = createSnapshot({
    revision: 3,
    playback: "paused",
    artist: longState.track.artist,
    title: longState.track.title,
  });
  await page.evaluate(
    (snapshot) => window.__emitOverlayState(snapshot),
    pausedState,
  );
  await page.waitForFunction(
    () =>
      document.getElementById("now-playing")?.style.transform ===
      "translateX(-500px)",
  );
  const pausedTransforms = await page.evaluate(() => ({
    root: document.getElementById("now-playing")?.style.transform,
    scale: document
      .getElementById("overlay-stage")
      ?.style.getPropertyValue("--overlay-scale"),
  }));
  assert(
    pausedTransforms.root === "translateX(-500px)",
    "Pause did not hide the widget.",
  );
  assert(
    pausedTransforms.scale === "3",
    "Pause changed the independent stage scale.",
  );

  const resumedState = createSnapshot({
    revision: 4,
    artist: longState.track.artist,
    title: longState.track.title,
  });
  await page.evaluate(
    (snapshot) => window.__emitOverlayState(snapshot),
    resumedState,
  );
  await page.waitForFunction(
    () =>
      document.getElementById("now-playing")?.style.transform ===
      "translateX(0px)",
  );
  assert(
    (await page.locator("#artist-text").textContent()) ===
      longState.track.artist,
    "Pause/resume changed visible text.",
  );

  const intermediateState = createSnapshot({
    revision: 5,
    artist: "Intermediate Artist",
    title: "Intermediate Track",
    artworkId: artworkIds.delayed,
  });
  const finalState = createSnapshot({
    revision: 6,
    artist: "Final Artist",
    title: "Final Track",
    artworkId: artworkIds.final,
  });
  await page.evaluate(
    (snapshot) => window.__emitOverlayState(snapshot),
    intermediateState,
  );
  await page.waitForTimeout(25);
  await page.evaluate(
    (snapshot) => window.__emitOverlayState(snapshot),
    finalState,
  );
  await page.waitForFunction(
    ({ artist, title, artworkId }) => {
      const artwork = document.getElementById("artwork-base");
      return (
        document.getElementById("artist-text")?.textContent === artist &&
        document.getElementById("track-text")?.textContent === title &&
        artwork instanceof HTMLImageElement &&
        !artwork.hidden &&
        artwork.src.endsWith(artworkId)
      );
    },
    {
      artist: finalState.track.artist,
      title: finalState.track.title,
      artworkId: artworkIds.final,
    },
  );
  await page.waitForTimeout(delayedArtworkMs + 100);
  const finalVisualState = await page.evaluate(() => {
    const artwork = document.getElementById("artwork-base");
    return {
      artist: document.getElementById("artist-text")?.textContent,
      artistMarquee: document
        .getElementById("artist")
        ?.classList.contains("is-marquee"),
      artwork: artwork instanceof HTMLImageElement ? artwork.src : null,
      artworkHidden:
        artwork instanceof HTMLImageElement ? artwork.hidden : null,
      overlayArtworkHidden: document.getElementById("artwork-overlay")?.hidden,
      rootTransform: document.getElementById("now-playing")?.style.transform,
      track: document.getElementById("track-text")?.textContent,
      trackMarquee: document
        .getElementById("track")
        ?.classList.contains("is-marquee"),
    };
  });
  assert(
    finalVisualState.artist === finalState.track.artist,
    "Rapid switch kept stale artist text.",
  );
  assert(
    finalVisualState.track === finalState.track.title,
    "Rapid switch kept stale track text.",
  );
  assert(
    finalVisualState.artwork?.endsWith(artworkIds.final),
    "Late artwork replaced final artwork.",
  );
  assert(finalVisualState.artworkHidden === false, "Final artwork is hidden.");
  assert(
    finalVisualState.overlayArtworkHidden === true,
    "Artwork transition did not settle.",
  );
  assert(
    finalVisualState.rootTransform === "translateX(0px)",
    "Final playing state is hidden.",
  );
  assert(
    finalVisualState.artistMarquee === false,
    "Final short artist remained in marquee mode.",
  );
  assert(
    finalVisualState.trackMarquee === false,
    "Final short track remained in marquee mode.",
  );

  return {
    finalState: finalVisualState,
    layoutResults,
    marqueeResize: {
      afterCurrentTime: marqueeAfterResize.currentTime,
      beforeCurrentTime: marqueeBeforeResize.currentTime,
      startTime: marqueeAfterResize.animationStart,
    },
    result: "OV2-03 browser layout regression passed",
  };
}
