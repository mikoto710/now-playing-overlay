import { describe, expect, it } from "vitest";
import { config } from "../src/config";

describe("overlay config", () => {
  it("uses only the version 3 local protocol endpoints", () => {
    expect({
      appearanceUrl: config.appearanceUrl,
      stateUrl: config.stateUrl,
      eventsUrl: config.eventsUrl,
    }).toEqual({
      appearanceUrl: "/api/v3/appearance",
      stateUrl: "/api/v3/state",
      eventsUrl: "/api/v3/events",
    });
  });
});
