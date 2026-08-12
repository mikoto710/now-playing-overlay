import { overlaySize } from "../config";

const overlayScaleProperty = "--overlay-scale";

interface OverlayScaleTarget {
  style: Pick<CSSStyleDeclaration, "setProperty">;
}

interface OverlayViewport {
  readonly innerWidth: number;
  readonly innerHeight: number;
  addEventListener(type: "resize", listener: () => void): void;
  removeEventListener(type: "resize", listener: () => void): void;
}

export function calculateOverlayScale(viewportWidth: number, viewportHeight: number): number {
  const width = normalizeLength(viewportWidth);
  const height = normalizeLength(viewportHeight);
  return Math.min(width / overlaySize.width, height / overlaySize.height);
}

export function calculateLogicalLength(
  renderedLength: number,
  renderedReferenceLength: number,
  logicalReferenceLength: number,
): number {
  const rendered = normalizeLength(renderedLength);
  const renderedReference = normalizeLength(renderedReferenceLength);
  const logicalReference = normalizeLength(logicalReferenceLength);
  if (renderedReference === 0 || logicalReference === 0) {
    return 0;
  }

  return normalizeLength((rendered * logicalReference) / renderedReference);
}

export function bindOverlayScaler(
  target: OverlayScaleTarget,
  viewport: OverlayViewport,
): () => void {
  const updateScale = (): void => {
    const scale = calculateOverlayScale(viewport.innerWidth, viewport.innerHeight);
    target.style.setProperty(overlayScaleProperty, scale.toString());
  };

  updateScale();
  viewport.addEventListener("resize", updateScale);
  return () => viewport.removeEventListener("resize", updateScale);
}

function normalizeLength(value: number): number {
  return Number.isFinite(value) ? Math.max(0, value) : 0;
}
