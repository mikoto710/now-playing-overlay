import { describe, expect, it, vi } from "vitest";
import {
  ArtworkLoader,
  preloadArtwork,
  type ArtworkImageFactory,
  type ArtworkTarget,
} from "../src/services/artwork-loader";

class FakeImage {
  naturalWidth = 0;
  naturalHeight = 0;
  complete = false;
  src = "";
  decode: (() => Promise<void>) | undefined;
  private readonly listeners = new Map<string, Set<() => void>>();

  constructor(hasDecode = false) {
    if (hasDecode) {
      this.decode = async () => undefined;
    }
  }

  addEventListener(type: string, listener: () => void): void {
    const listeners = this.listeners.get(type) ?? new Set<() => void>();
    listeners.add(listener);
    this.listeners.set(type, listeners);
  }

  removeEventListener(type: string, listener: () => void): void {
    this.listeners.get(type)?.delete(listener);
  }

  succeed(): void {
    this.complete = true;
    this.naturalWidth = 300;
    this.naturalHeight = 300;
    this.emit("load");
  }

  fail(): void {
    this.complete = true;
    this.emit("error");
  }

  private emit(type: string): void {
    for (const listener of [...(this.listeners.get(type) ?? [])]) {
      listener();
    }
  }
}

describe("preloadArtwork", () => {
  it("uses the load event when Image.decode is unavailable", async () => {
    const image = new FakeImage();
    const loading = preloadArtwork("/artwork", asFactory(image));

    image.succeed();

    await expect(loading).resolves.toBeUndefined();
  });

  it("rejects the error fallback without accepting zero-sized artwork", async () => {
    const image = new FakeImage();
    const loading = preloadArtwork("/broken", asFactory(image));

    image.fail();

    await expect(loading).rejects.toThrow("Artwork could not be loaded");
  });
});

describe("ArtworkLoader", () => {
  it("uses latest-wins when an older cover finishes last", async () => {
    const images: FakeImage[] = [];
    const target = createTarget();
    const loader = new ArtworkLoader(target, () => {
      const image = new FakeImage();
      images.push(image);
      return image as unknown as HTMLImageElement;
    });

    const oldUpdate = loader.update("/old", true);
    const latestUpdate = loader.update("/latest", true);
    images[1]?.succeed();
    await latestUpdate;
    images[0]?.succeed();
    await oldUpdate;

    expect(target.replaceArtwork).toHaveBeenCalledOnce();
    expect(target.replaceArtwork).toHaveBeenCalledWith("/latest", expect.any(Function));
  });

  it("keeps the placeholder after a new-track artwork failure", async () => {
    const image = new FakeImage();
    const target = createTarget();
    const reportError = vi.fn();
    const loader = new ArtworkLoader(target, asFactory(image), reportError);

    const update = loader.update("/broken", true);
    image.fail();
    await update;

    expect(target.clearArtwork).toHaveBeenCalledOnce();
    expect(target.replaceArtwork).not.toHaveBeenCalled();
    expect(reportError).toHaveBeenCalledOnce();
  });
});

function asFactory(image: FakeImage): ArtworkImageFactory {
  return () => image as unknown as HTMLImageElement;
}

function createTarget(): ArtworkTarget & {
  clearArtwork: ReturnType<typeof vi.fn>;
  replaceArtwork: ReturnType<typeof vi.fn>;
} {
  return {
    clearArtwork: vi.fn(),
    replaceArtwork: vi.fn(async () => true),
  };
}
