import { describe, expect, it } from "vitest";
import { isCacheable, isFresh } from "./portraitCache";

const hour = 60 * 60 * 1000;

describe("portrait cache", () => {
  it("only keeps found and not found portraits", () => {
    expect(isCacheable(200)).toBe(true);
    expect(isCacheable(404)).toBe(true);
    expect(isCacheable(429)).toBe(false);
    expect(isCacheable(500)).toBe(false);
  });

  it("keeps portraits for a week and misses for an hour", () => {
    expect(isFresh(200, 0, 6 * 24 * hour)).toBe(true);
    expect(isFresh(200, 0, 8 * 24 * hour)).toBe(false);
    expect(isFresh(404, 0, hour / 2)).toBe(true);
    expect(isFresh(404, 0, 2 * hour)).toBe(false);
  });
});
