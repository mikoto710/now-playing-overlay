import { describe, expect, it } from "vitest";
import {
  bindOverlayScaler,
  calculateLogicalLength,
  calculateOverlayScale,
} from "../src/ui/overlay-scaler";

describe("calculateOverlayScale", () => {
  it.each([
    [350, 70, 1],
    [700, 140, 2],
    [1050, 210, 3],
  ])("scales a %d x %d 5:1 viewport by %d", (width, height, expected) => {
    expect(calculateOverlayScale(width, height)).toBe(expected);
  });

  it("uses width as the constraint in a taller-than-5:1 viewport", () => {
    expect(calculateOverlayScale(700, 200)).toBe(2);
  });

  it("uses height as the constraint in a wider-than-5:1 viewport", () => {
    expect(calculateOverlayScale(1000, 140)).toBe(2);
  });

  it.each([
    [-1, 70],
    [350, -1],
    [Number.NaN, 70],
    [350, Number.NaN],
    [Number.POSITIVE_INFINITY, 70],
    [350, Number.POSITIVE_INFINITY],
  ])("returns a finite non-negative scale for an invalid %s x %s viewport", (width, height) => {
    const scale = calculateOverlayScale(width, height);

    expect(Number.isFinite(scale)).toBe(true);
    expect(scale).toBeGreaterThanOrEqual(0);
  });
});

describe("calculateLogicalLength", () => {
  it.each([
    [123.75, 280, 280],
    [247.5, 560, 280],
    [61.875, 140, 280],
  ])(
    "preserves a 123.75px logical length from rendered length %d and reference %d",
    (renderedLength, renderedReferenceLength, logicalReferenceLength) => {
      expect(
        calculateLogicalLength(renderedLength, renderedReferenceLength, logicalReferenceLength),
      ).toBe(123.75);
    },
  );

  it.each([
    [Number.NaN, 280, 280],
    [100, 0, 280],
    [100, Number.POSITIVE_INFINITY, 280],
    [100, 280, -1],
  ])(
    "returns zero for invalid measurement inputs",
    (rendered, renderedReference, logicalReference) => {
      expect(calculateLogicalLength(rendered, renderedReference, logicalReference)).toBe(0);
    },
  );
});

describe("bindOverlayScaler", () => {
  it("updates immediately and on resize, then removes its listener", () => {
    const properties = new Map<string, string>();
    const target = {
      style: {
        setProperty: (name: string, value: string) => properties.set(name, value),
      },
    };
    const viewport = new FakeViewport(350, 70);

    const stop = bindOverlayScaler(target, viewport);
    expect(properties.get("--overlay-scale")).toBe("1");

    viewport.resizeTo(700, 140);
    expect(properties.get("--overlay-scale")).toBe("2");

    stop();
    expect(viewport.listenerCount).toBe(0);
    viewport.resizeTo(1050, 210);
    expect(properties.get("--overlay-scale")).toBe("2");
  });

  it("does not multiply the CSS scale by devicePixelRatio", () => {
    const properties = new Map<string, string>();
    const target = {
      style: {
        setProperty: (name: string, value: string) => properties.set(name, value),
      },
    };
    const viewport = new FakeViewport(350, 70, 2);

    bindOverlayScaler(target, viewport);

    expect(properties.get("--overlay-scale")).toBe("1");
  });
});

class FakeViewport {
  private readonly resizeListeners = new Set<() => void>();

  constructor(
    public innerWidth: number,
    public innerHeight: number,
    public readonly devicePixelRatio = 1,
  ) {}

  get listenerCount(): number {
    return this.resizeListeners.size;
  }

  addEventListener(_type: "resize", listener: () => void): void {
    this.resizeListeners.add(listener);
  }

  removeEventListener(_type: "resize", listener: () => void): void {
    this.resizeListeners.delete(listener);
  }

  resizeTo(width: number, height: number): void {
    this.innerWidth = width;
    this.innerHeight = height;
    for (const listener of this.resizeListeners) {
      listener();
    }
  }
}
