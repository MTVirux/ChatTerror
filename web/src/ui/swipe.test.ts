import { describe, expect, it } from "vitest";
import { ignoresSwipe, swipeResult } from "./swipe";

describe("swipeResult", () => {
  it("opens on a rightward swipe starting anywhere", () => {
    expect(swipeResult({ dx: 60, dy: 5, open: false })).toBe("open");
    expect(swipeResult({ dx: 80, dy: 0, open: false })).toBe("open");
  });
  it("ignores swipes that are too short", () => {
    expect(swipeResult({ dx: 30, dy: 0, open: false })).toBeNull();
    expect(swipeResult({ dx: -60, dy: 0, open: false })).toBeNull();
  });
  it("ignores mostly vertical movement so scrolling never opens the drawer", () => {
    expect(swipeResult({ dx: 50, dy: 80, open: false })).toBeNull();
  });
  it("closes on a leftward swipe anywhere when open", () => {
    expect(swipeResult({ dx: -50, dy: 4, open: true })).toBe("close");
    expect(swipeResult({ dx: 50, dy: 4, open: true })).toBeNull();
  });
});

describe("ignoresSwipe", () => {
  it("ignores swipes from text inputs and selects", () => {
    expect(ignoresSwipe("TEXTAREA")).toBe(true);
    expect(ignoresSwipe("INPUT")).toBe(true);
    expect(ignoresSwipe("SELECT")).toBe(true);
  });
  it("allows swipes from anything else", () => {
    expect(ignoresSwipe("DIV")).toBe(false);
    expect(ignoresSwipe("BUTTON")).toBe(false);
  });
});
