import { describe, expect, it } from "vitest";
import { overlaySize } from "../src/config";

describe("overlaySize", () => {
  it("keeps the first-release OBS viewport fixed at 350 by 70", () => {
    expect(overlaySize).toEqual({ width: 350, height: 70 });
  });
});
