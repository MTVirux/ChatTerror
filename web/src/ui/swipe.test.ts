import { describe, expect, it } from "vitest";
import { swipeResult } from "./swipe";

describe("swipeResult", () => {
  it("opens on a rightward swipe from the left edge", () => {
    expect(swipeResult({ startX: 10, dx: 60, dy: 5, open: false })).toBe("open");
  });
  it("ignores swipes that start away from the edge or are too short", () => {
    expect(swipeResult({ startX: 100, dx: 80, dy: 0, open: false })).toBeNull();
    expect(swipeResult({ startX: 10, dx: 30, dy: 0, open: false })).toBeNull();
  });
  it("ignores mostly vertical movement so scrolling never opens the drawer", () => {
    expect(swipeResult({ startX: 10, dx: 50, dy: 80, open: false })).toBeNull();
  });
  it("closes on a leftward swipe anywhere when open", () => {
    expect(swipeResult({ startX: 200, dx: -50, dy: 4, open: true })).toBe("close");
    expect(swipeResult({ startX: 200, dx: 50, dy: 4, open: true })).toBeNull();
  });
});
