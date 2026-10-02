import { describe, expect, it } from "vitest";
import { channelNotifyPrefs, EMPTY_CHANNEL_PREFS, notifyChoice, setNotify, toggleMuted, togglePinned } from "./channelPrefs";

const say = "c|Alpha Beta|say";
const tell = "t|Alpha Beta|Foo Bar@World";

describe("channel prefs", () => {
  it("toggles pins and mutes in the order they were added", () => {
    const pinned = togglePinned(togglePinned(EMPTY_CHANNEL_PREFS, tell), say);
    expect(pinned.pinned).toEqual([tell, say]);
    expect(togglePinned(pinned, tell).pinned).toEqual([say]);
    expect(toggleMuted(toggleMuted(EMPTY_CHANNEL_PREFS, say), say).muted).toEqual([]);
  });

  it("stores only non default notify choices", () => {
    const all = setNotify(EMPTY_CHANNEL_PREFS, say, "all");
    expect(notifyChoice(all, say)).toBe("all");
    expect(notifyChoice(all, tell)).toBe("default");
    expect(setNotify(all, say, "default").notify).toEqual({});
  });

  it("converts to payload channels with mute winning", () => {
    const prefs = { pinned: [say], muted: [say], notify: { [say]: "all" as const, [tell]: "all" as const } };
    expect(channelNotifyPrefs(prefs)).toEqual([
      { character: "Alpha Beta", channel: "say", notify: "none" },
      { character: "Alpha Beta", channel: "tell", partner: "Foo Bar@World", notify: "all" },
    ]);
  });

  it("does not send pins or unknown keys", () => {
    expect(channelNotifyPrefs({ pinned: [say], muted: ["c|A|bogus", "x"], notify: {} })).toEqual([]);
  });
});
