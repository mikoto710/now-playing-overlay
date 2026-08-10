export interface ArtworkTarget {
  clearArtwork(): void;
  replaceArtwork(url: string, isRequestCurrent: () => boolean): Promise<boolean>;
}

export type ArtworkImageFactory = () => HTMLImageElement;

export async function preloadArtwork(
  url: string,
  createImage: ArtworkImageFactory = () => new Image(),
): Promise<void> {
  const image = createImage();

  await new Promise<void>((resolve, reject) => {
    let settled = false;
    const cleanup = (): void => {
      image.removeEventListener("load", handleLoad);
      image.removeEventListener("error", handleError);
    };
    const succeed = (): void => {
      if (settled) {
        return;
      }
      if (image.naturalWidth === 0 || image.naturalHeight === 0) {
        fail(new Error("Artwork has invalid dimensions."));
        return;
      }
      settled = true;
      cleanup();
      resolve();
    };
    const fail = (error: Error): void => {
      if (settled) {
        return;
      }
      settled = true;
      cleanup();
      reject(error);
    };
    const handleLoad = (): void => succeed();
    const handleError = (): void => fail(new Error("Artwork could not be loaded."));

    image.addEventListener("load", handleLoad, { once: true });
    image.addEventListener("error", handleError, { once: true });
    image.src = url;

    if (typeof image.decode === "function") {
      void image.decode().then(succeed, (reason: unknown) => {
        // Some CEF builds reject decode() despite completing the image load. In that case the
        // already-installed load/error handlers remain the reliable fallback.
        if (image.complete) {
          if (image.naturalWidth > 0 && image.naturalHeight > 0) {
            succeed();
          } else {
            fail(reason instanceof Error ? reason : new Error("Artwork could not be decoded."));
          }
        }
      });
    }
  });
}

export class ArtworkLoader {
  private requestRevision = 0;

  constructor(
    private readonly target: ArtworkTarget,
    private readonly createImage: ArtworkImageFactory = () => new Image(),
    private readonly reportError: (error: unknown) => void = (error) =>
      console.warn("Artwork update failed.", error),
  ) {}

  async update(url: string | null, clearBeforeLoad: boolean): Promise<void> {
    const requestRevision = ++this.requestRevision;
    if (clearBeforeLoad || url === null) {
      this.target.clearArtwork();
    }
    if (url === null) {
      return;
    }

    try {
      await preloadArtwork(url, this.createImage);
      if (requestRevision !== this.requestRevision) {
        return;
      }
      await this.target.replaceArtwork(url, () => requestRevision === this.requestRevision);
    } catch (error) {
      if (requestRevision === this.requestRevision) {
        this.reportError(error);
      }
    }
  }
}
