import { config } from "../config";
import { delay, finishAnimation } from "./animations";

export interface TrackText {
  artist: string;
  track: string;
}

interface TextVisualState {
  marginLeft: string;
  opacity: string;
  transform: string;
}

export class NowPlayingWidget {
  private readonly root = this.requireElement<HTMLElement>("now-playing");
  private readonly details = this.requireElement<HTMLElement>("track-details");
  private readonly artist = this.requireElement<HTMLParagraphElement>("artist");
  private readonly track = this.requireElement<HTMLParagraphElement>("track");
  private readonly artistText = this.requireElement<HTMLSpanElement>("artist-text");
  private readonly artistCopy = this.requireElement<HTMLSpanElement>("artist-copy");
  private readonly trackText = this.requireElement<HTMLSpanElement>("track-text");
  private readonly trackCopy = this.requireElement<HTMLSpanElement>("track-copy");
  private readonly artworkBase = this.requireElement<HTMLImageElement>("artwork-base");
  private readonly artworkOverlay = this.requireElement<HTMLImageElement>("artwork-overlay");
  private textRevision = 0;
  private pendingText: TrackText | null = null;
  private textTransition: Promise<void> | null = null;
  private lastRequestedText: TrackText | null = null;
  private artworkRevision = 0;
  private currentArtworkUrl: string | null = null;
  private widgetAnimation: Animation | null = null;
  private artworkAnimation: Animation | null = null;

  show(): void {
    this.animateWidget("translateX(0)");
  }

  hide(): void {
    // Text and artwork keep converging while hidden so pause/resume never replays a transition.
    this.animateWidget("translateX(-500px)");
  }

  updateText(next: TrackText): void {
    if (
      this.lastRequestedText?.artist === next.artist &&
      this.lastRequestedText.track === next.track
    ) {
      return;
    }

    this.lastRequestedText = next;
    this.pendingText = next;
    this.textRevision += 1;
    this.freezeCurrentTextAnimations();
    if (this.textTransition === null) {
      this.textTransition = this.drainTextUpdates();
    }
  }

  clearArtwork(): void {
    this.artworkRevision += 1;
    this.artworkAnimation?.cancel();
    this.artworkAnimation = null;
    this.currentArtworkUrl = null;
    for (const image of [this.artworkBase, this.artworkOverlay]) {
      image.hidden = true;
      image.removeAttribute("src");
      image.removeAttribute("style");
    }
  }

  async replaceArtwork(url: string, isRequestCurrent: () => boolean): Promise<boolean> {
    if (this.currentArtworkUrl === url) {
      return true;
    }

    const revision = ++this.artworkRevision;
    this.artworkAnimation?.cancel();
    this.artworkOverlay.src = url;
    this.artworkOverlay.hidden = false;
    const animation = this.artworkOverlay.animate([{ opacity: 0 }, { opacity: 1 }], {
      duration: config.artworkFadeMs,
      easing: config.animationEasing,
      fill: "forwards",
    });
    this.artworkAnimation = animation;
    await finishAnimation(animation);

    if (revision !== this.artworkRevision || !isRequestCurrent()) {
      if (this.artworkAnimation === animation) {
        animation.cancel();
        this.artworkAnimation = null;
        this.artworkOverlay.hidden = true;
        this.artworkOverlay.removeAttribute("src");
        this.artworkOverlay.removeAttribute("style");
      }
      return false;
    }

    this.artworkBase.src = url;
    this.artworkBase.hidden = false;
    this.artworkOverlay.hidden = true;
    this.artworkOverlay.removeAttribute("style");
    this.currentArtworkUrl = url;
    animation.cancel();
    this.artworkAnimation = null;
    return true;
  }

  private async drainTextUpdates(): Promise<void> {
    try {
      while (this.pendingText !== null) {
        const next = this.pendingText;
        this.pendingText = null;
        await this.transitionText(next, this.textRevision);
      }
    } finally {
      this.textTransition = null;
    }
  }

  private async transitionText(next: TrackText, revision: number): Promise<void> {
    const [artistState, trackState] = this.freezeCurrentTextAnimations();
    const exit = [this.artist, this.track].map((element) =>
      element.animate(
        [
          {
            marginLeft: element === this.artist ? artistState.marginLeft : trackState.marginLeft,
            opacity: element === this.artist ? artistState.opacity : trackState.opacity,
          },
          { marginLeft: "-100px", opacity: 0 },
        ],
        {
          duration: config.textExitAnimationMs,
          easing: config.animationEasing,
          fill: "both",
        },
      ),
    );
    await Promise.all(exit.map(finishAnimation));
    if (revision !== this.textRevision) {
      return;
    }

    for (const [index, element] of [this.artist, this.track].entries()) {
      element.style.marginLeft = "-100px";
      element.style.opacity = "0";
      exit[index]?.cancel();
    }
    this.clearMarquee();
    this.artistText.textContent = next.artist;
    this.trackText.textContent = next.track;
    this.artistCopy.textContent = "";
    this.trackCopy.textContent = "";
    this.clearTextTransforms();
    await delay(config.textEnterDelayMs);
    if (revision !== this.textRevision) {
      return;
    }
    await Promise.all([
      this.enterText(this.artist, this.artistText, this.artistCopy, revision),
      this.enterText(this.track, this.trackText, this.trackCopy, revision),
    ]);
  }

  private animateWidget(transform: string): void {
    const currentTransform = getComputedStyle(this.root).transform;
    this.widgetAnimation?.cancel();
    const animation = this.root.animate(
      [
        { transform: currentTransform === "none" ? "translateX(-500px)" : currentTransform },
        { transform },
      ],
      {
        duration: config.widgetAnimationMs,
        easing: config.animationEasing,
        fill: "both",
      },
    );
    this.widgetAnimation = animation;
    void finishAnimation(animation).then(() => {
      if (this.widgetAnimation !== animation) {
        return;
      }
      this.root.style.transform = transform;
      animation.cancel();
      this.widgetAnimation = null;
    });
  }

  private async enterText(
    element: HTMLParagraphElement,
    text: HTMLSpanElement,
    copy: HTMLSpanElement,
    revision: number,
  ): Promise<void> {
    const textWidth = text.getBoundingClientRect().width;
    const isWide = textWidth > this.details.clientWidth - config.marqueeThresholdOffsetPx;
    if (isWide) {
      await this.enterMarquee(element, text, copy, textWidth, revision);
      return;
    }
    const animation = element.animate(
      [
        { marginLeft: "-100px", opacity: 0 },
        { marginLeft: "7px", opacity: 1 },
      ],
      {
        duration: config.textEnterAnimationMs,
        easing: config.animationEasing,
        fill: "both",
      },
    );
    await finishAnimation(animation);
    if (revision !== this.textRevision) {
      return;
    }
    element.style.marginLeft = "7px";
    element.style.opacity = "1";
    animation.cancel();
  }

  private async enterMarquee(
    element: HTMLParagraphElement,
    text: HTMLSpanElement,
    copy: HTMLSpanElement,
    textWidth: number,
    revision: number,
  ): Promise<void> {
    element.style.marginLeft = "7px";
    element.style.opacity = "0";
    if (revision !== this.textRevision) {
      return;
    }

    copy.textContent = text.textContent;
    element.style.setProperty("--marquee-gap", `${config.marqueeGapPx}px`);
    element.classList.add("is-marquee");

    const pixelsPerMillisecond = (textWidth + config.marqueeStartPx) / config.marqueeDurationMs;
    const introDuration = config.marqueeStartPx / pixelsPerMillisecond;
    const loopDistance = textWidth + config.marqueeGapPx;
    const loopDuration = loopDistance / pixelsPerMillisecond;
    const marqueeStart = `translateX(${config.marqueeStartPx}px)`;

    element.style.transform = marqueeStart;
    void element.offsetWidth;
    const intro = element.animate([{ transform: marqueeStart }, { transform: "translateX(0)" }], {
      duration: introDuration,
      easing: "linear",
      fill: "both",
    });
    const fade = element.animate([{ opacity: 0 }, { opacity: 1 }], {
      duration: config.textExitAnimationMs,
      easing: config.animationEasing,
      fill: "both",
    });

    await finishAnimation(intro);
    if (revision !== this.textRevision) {
      return;
    }
    element.style.transform = "translateX(0)";
    intro.cancel();
    element.animate(
      [{ transform: "translateX(0)" }, { transform: `translateX(-${loopDistance}px)` }],
      {
        duration: loopDuration,
        easing: "linear",
        iterations: Infinity,
      },
    );

    await finishAnimation(fade);
    if (revision !== this.textRevision) {
      return;
    }
    element.style.opacity = "1";
    fade.cancel();
  }

  private clearMarquee(): void {
    for (const element of [this.artist, this.track]) {
      element.classList.remove("is-marquee");
      element.style.removeProperty("--marquee-gap");
    }
  }

  private clearTextTransforms(): void {
    this.artist.style.removeProperty("transform");
    this.track.style.removeProperty("transform");
  }

  private freezeCurrentTextAnimations(): [TextVisualState, TextVisualState] {
    const artistState = this.readTextVisualState(this.artist);
    const trackState = this.readTextVisualState(this.track);
    this.cancelTextAnimations();
    this.freezeTextState(this.artist, artistState);
    this.freezeTextState(this.track, trackState);
    return [artistState, trackState];
  }

  private readTextVisualState(element: HTMLParagraphElement): TextVisualState {
    const style = getComputedStyle(element);
    return {
      marginLeft: style.marginLeft,
      opacity: style.opacity,
      transform: style.transform,
    };
  }

  private freezeTextState(element: HTMLParagraphElement, state: TextVisualState): void {
    element.style.marginLeft = state.marginLeft;
    element.style.opacity = state.opacity;
    if (state.transform === "none") {
      element.style.removeProperty("transform");
      return;
    }
    element.style.transform = state.transform;
  }

  private cancelTextAnimations(): void {
    for (const element of [this.artist, this.track]) {
      for (const animation of element.getAnimations()) {
        animation.cancel();
      }
    }
  }

  private requireElement<T extends HTMLElement>(id: string): T {
    const element = document.getElementById(id);
    if (!(element instanceof HTMLElement)) {
      throw new Error(`Missing required element: #${id}`);
    }
    return element as T;
  }
}
