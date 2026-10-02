import { describe, expect, it } from "vitest";
import {
  channelIncludes,
  channelNotifyPrefs,
  deleteCustom,
  EMPTY_CHANNEL_PREFS,
  isItemMuted,
  moveChannel,
  notifyChoice,
  saveCustom,
  setNotify,
  toggleMuted,
  togglePinned,
  withDefaults,
  type CustomChannel,
} from "./channelPrefs";

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
    const prefs = { pinned: [say], muted: [say], notify: { [say]: "all" as const, [tell]: "all" as const }, custom: [], order: {} };
    expect(channelNotifyPrefs(prefs)).toEqual([
      { character: "Alpha Beta", channel: "say", notify: "none" },
      { character: "Alpha Beta", channel: "tell", partner: "Foo Bar@World", notify: "all" },
    ]);
  });

  it("does not send pins or unknown keys", () => {
    expect(channelNotifyPrefs({ pinned: [say], muted: ["c|A|bogus", "x"], notify: {}, custom: [], order: {} })).toEqual([]);
  });

  it("fills in missing fields and drops plain channel keys from older prefs", () => {
    const old = { pinned: [say, tell], muted: [say], notify: { [say]: "all" as const, [tell]: "none" as const } };
    expect(withDefaults(old)).toEqual({ pinned: [tell], muted: [], notify: { [tell]: "none" }, custom: [], order: {} });
  });
});

describe("custom channels", () => {
  const social: CustomChannel = { id: "s", character: "Alpha Beta", name: "Social", channels: ["freeCompany", "say", "tell"] };
  const key = "x|Alpha Beta|s";
  const prefs = saveCustom(EMPTY_CHANNEL_PREFS, social);

  it("deletes a channel for a character named like an Object.prototype key", () => {
    for (const character of ["__proto__", "constructor"]) {
      const odd = { ...social, character };
      expect(deleteCustom(saveCustom(EMPTY_CHANNEL_PREFS, odd), odd).custom).toEqual([]);
    }
  });

  it("adds, edits and deletes along with their prefs", () => {
    expect(saveCustom(prefs, { ...social, name: "Chat" }).custom).toEqual([{ ...social, name: "Chat" }]);
    const decorated = { ...togglePinned(toggleMuted(setNotify(prefs, key, "all"), key), key), order: { "Alpha Beta": [key, tell] } };
    expect(deleteCustom(decorated, social)).toEqual({ pinned: [], muted: [], notify: {}, custom: [], order: { "Alpha Beta": [tell] } });
  });

  it("includes member channels and every tell of its character", () => {
    expect(channelIncludes(prefs, key, say)).toBe(true);
    expect(channelIncludes(prefs, key, tell)).toBe(true);
    expect(channelIncludes(prefs, key, "c|Alpha Beta|party")).toBe(false);
    expect(channelIncludes(prefs, key, "c|Other One|say")).toBe(false);
    expect(channelIncludes(prefs, tell, tell)).toBe(true);
  });

  it("mutes members of a muted custom channel", () => {
    expect(isItemMuted(prefs, say)).toBe(false);
    const muted = toggleMuted(prefs, key);
    expect(isItemMuted(muted, say)).toBe(true);
    expect(isItemMuted(muted, tell)).toBe(true);
    expect(isItemMuted(muted, "c|Alpha Beta|party")).toBe(false);
  });

  it("sends a custom channel's choice for its chat channels only", () => {
    expect(channelNotifyPrefs(setNotify(prefs, key, "all"))).toEqual([
      { character: "Alpha Beta", channel: "freeCompany", notify: "all" },
      { character: "Alpha Beta", channel: "say", notify: "all" },
    ]);
    expect(channelNotifyPrefs(toggleMuted(setNotify(prefs, key, "all"), key)).map((p) => p.notify)).toEqual(["none", "none"]);
  });

  it("moves a row one step within the shown order", () => {
    const other = "t|Alpha Beta|X Y@W";
    expect(moveChannel(prefs, "Alpha Beta", [key, tell, other], tell, -1).order).toEqual({ "Alpha Beta": [tell, key, other] });
    expect(moveChannel(prefs, "Alpha Beta", [key, tell, other], key, -1)).toBe(prefs);
  });
});
