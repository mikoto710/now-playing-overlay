import { overlaySize } from "../config";

const overlayScaleProperty = "--overlay-scale";
const overlayPreviewScaleParameter = "previewScale";
const supportedOverlayPreviewScales = new Map([
  ["1", 1],
  ["2", 2],
  ["3", 3],
  ["4", 4],
  ["5", 5],
]);

interface OverlayScaleTarget {
  style: Pick<CSSStyleDeclaration, "setProperty">;
}

interface OverlayViewport {
  readonly innerWidth: number;
  readonly innerHeight: number;
  addEventListener(type: "resize", listener: () => void): void;
  removeEventListener(type: "resize", listener: () => void): void;
}

export function calculateOverlayScale(
  viewportWidth: number,
  viewportHeight: number,
  maximumScale = Number.POSITIVE_INFINITY,
): number {
  const width = normalizeLength(viewportWidth);
  const height = normalizeLength(viewportHeight);
  const normalizedMaximumScale = Number.isFinite(maximumScale)
    ? Math.max(0, maximumScale)
    : Number.POSITIVE_INFINITY;
  return Math.min(width / overlaySize.width, height / overlaySize.height, normalizedMaximumScale);
}

export function parseOverlayPreviewScale(search: string): number | null {
  const values = new URLSearchParams(search).getAll(overlayPreviewScaleParameter);
  if (values.length !== 1) {
    return null;
  }

  return supportedOverlayPreviewScales.get(values[0] ?? "") ?? null;
}

export function preserveOverlayPreviewUrl(overlayUrl: string, currentSearch: string): string {
  const previewScale = parseOverlayPreviewScale(currentSearch);
  if (previewScale === null) {
    return overlayUrl;
  }

  const redirectUrl = new URL(overlayUrl);
  redirectUrl.searchParams.set(overlayPreviewScaleParameter, previewScale.toString());
  return redirectUrl.href;
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
  maximumScale = Number.POSITIVE_INFINITY,
): () => void {
  const updateScale = (): void => {
    const scale = calculateOverlayScale(viewport.innerWidth, viewport.innerHeight, maximumScale);
    target.style.setProperty(overlayScaleProperty, scale.toString());
  };

  updateScale();
  viewport.addEventListener("resize", updateScale);
  return () => viewport.removeEventListener("resize", updateScale);
}

function normalizeLength(value: number): number {
  return Number.isFinite(value) ? Math.max(0, value) : 0;
}
