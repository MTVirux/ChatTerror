import { describe, expect, it } from "vitest";
import { EMPTY_CHANNEL_PREFS, type ChannelPrefs } from "../core/channelPrefs";
import type { ChatItem } from "../core/protocol";
import { arrange, buildChannelTree, channelKey, channelSlug, firstChannel, inChannel, isCollapsed, itemKey, parseChannelKey, pinFirst, toggleCategory, type ChannelRef } from "./channels";

function msg(over: Partial<ChatItem>): ChatItem {
  return { id: Math.random().toString(), ts: 1, channel: "party", sender: "Y'shtola Rhul", senderWorld: "Twintania", text: "hi", character: "Alpha Beta", outgoing: false, ...over };
}

const prefs: ChannelPrefs = { ...EMPTY_CHANNEL_PREFS, custom: [{ id: "s", character: "Alpha Beta", name: "Social", channels: ["freeCompany", "linkshell1", "tell"] }] };
const socialRef: ChannelRef = { kind: "custom", character: "Alpha Beta", id: "s" };

describe("channel keys", () => {
  it("round trips custom and tell refs", () => {
    const tell = { kind: "tell", character: "Alpha Beta", partner: "Thancred Waters@Gilgamesh" } as const;
    expect(parseChannelKey(channelKey(socialRef))).toEqual(socialRef);
    expect(parseChannelKey(channelKey(tell))).toEqual(tell);
    expect(parseChannelKey("c|Alpha Beta|say")).toBeNull();
    expect(parseChannelKey("nonsense")).toBeNull();
  });

  it("puts both directions of a tell in one channel", () => {
    const incoming = msg({ channel: "tell", sender: "Thancred Waters", senderWorld: "Gilgamesh" });
    const outgoing = msg({ channel: "tell", sender: "Thancred Waters@Gilgamesh", senderWorld: undefined, outgoing: true });
    expect(itemKey(incoming)).toBe("t|Alpha Beta|Thancred Waters@Gilgamesh");
    expect(itemKey(outgoing)).toBe(itemKey(incoming));
  });

  it("keeps a tell partner with an unknown world", () => {
    expect(itemKey(msg({ channel: "tell", sender: "Thancred Waters", senderWorld: undefined }))).toBe("t|Alpha Beta|Thancred Waters");
  });

  it("slugs channel labels", () => {
    expect(channelSlug("freeCompany")).toBe("fc");
    expect(channelSlug("party")).toBe("party");
  });
});

describe("inChannel", () => {
  it("shows a custom channel's members of that character only", () => {
    expect(inChannel(msg({ channel: "freeCompany" }), socialRef, prefs)).toBe(true);
    expect(inChannel(msg({ channel: "tell", sender: "A B", senderWorld: "W" }), socialRef, prefs)).toBe(true);
    expect(inChannel(msg({ channel: "party" }), socialRef, prefs)).toBe(false);
    expect(inChannel(msg({ channel: "freeCompany", character: "Other One" }), socialRef, prefs)).toBe(false);
  });

  it("shows nothing for a deleted custom channel", () => {
    expect(inChannel(msg({ channel: "freeCompany" }), socialRef, EMPTY_CHANNEL_PREFS)).toBe(false);
  });

  it("shows one partner in a tell channel", () => {
    const tell: ChannelRef = { kind: "tell", character: "Alpha Beta", partner: "A B@W" };
    expect(inChannel(msg({ channel: "tell", sender: "A B", senderWorld: "W" }), tell, prefs)).toBe(true);
    expect(inChannel(msg({ channel: "tell", sender: "C D", senderWorld: "W" }), tell, prefs)).toBe(false);
  });
});

describe("buildChannelTree", () => {
  it("shows the logged-in character first with only custom channels and tells", () => {
    const items = [msg({ character: "Other One", ts: 50 }), msg({ channel: "say" }), msg({ channel: "tell", sender: "A B", senderWorld: "W", ts: 5 })];
    const tree = buildChannelTree(items, { character: "Alpha Beta", prefs });
    expect(tree.map((c) => [c.character, c.active])).toEqual([["Alpha Beta", true], ["Other One", false]]);
    expect(tree[0].refs).toEqual([socialRef, { kind: "tell", character: "Alpha Beta", partner: "A B@W" }]);
    expect(tree[1].refs).toEqual([]);
  });

  it("keeps a character that only has custom channels", () => {
    expect(buildChannelTree([], { prefs }).map((c) => c.character)).toEqual(["Alpha Beta"]);
  });

  it("orders other characters by latest activity and tells newest first", () => {
    const items = [
      msg({ character: "Old Char", ts: 10 }),
      msg({ character: "New Char", ts: 20 }),
      msg({ character: "New Char", channel: "tell", sender: "A B", senderWorld: "W", ts: 21 }),
      msg({ character: "New Char", channel: "tell", sender: "C D", senderWorld: "W", ts: 30 }),
    ];
    const tree = buildChannelTree(items, { prefs: EMPTY_CHANNEL_PREFS });
    expect(tree.map((c) => c.character)).toEqual(["New Char", "Old Char"]);
    expect(tree[0].refs.map(channelKey)).toEqual(["t|New Char|C D@W", "t|New Char|A B@W"]);
  });

  it("applies the saved order", () => {
    const items = [msg({ channel: "tell", sender: "A B", senderWorld: "W" })];
    const ordered = { ...prefs, order: { "Alpha Beta": ["t|Alpha Beta|A B@W", "x|Alpha Beta|s"] } };
    expect(buildChannelTree(items, { character: "Alpha Beta", prefs: ordered })[0].refs.map(channelKey)).toEqual(["t|Alpha Beta|A B@W", "x|Alpha Beta|s"]);
  });

  it("finds the first channel", () => {
    expect(firstChannel(buildChannelTree([], { character: "Alpha Beta", prefs }))).toEqual(socialRef);
    expect(firstChannel(buildChannelTree([], { character: "Alpha Beta", prefs: EMPTY_CHANNEL_PREFS }))).toBeNull();
    expect(firstChannel([])).toBeNull();
  });
});

describe("category collapse", () => {
  it("expands only the logged-in character by default", () => {
    expect(isCollapsed({ character: "A", active: true }, [])).toBe(false);
    expect(isCollapsed({ character: "B", active: false }, [])).toBe(true);
  });
  it("flips a toggled character and keeps new ones on the default", () => {
    const toggled = toggleCategory(toggleCategory([], "A"), "B");
    expect(isCollapsed({ character: "A", active: true }, toggled)).toBe(true);
    expect(isCollapsed({ character: "B", active: false }, toggled)).toBe(false);
    expect(isCollapsed({ character: "C", active: false }, toggled)).toBe(true);
  });
  it("toggles back to the default", () => {
    expect(toggleCategory(toggleCategory([], "A"), "A")).toEqual([]);
  });
});

describe("arrange", () => {
  const a: ChannelRef = { kind: "custom", character: "A", id: "a" };
  const b: ChannelRef = { kind: "custom", character: "A", id: "b" };
  const tell: ChannelRef = { kind: "tell", character: "A", partner: "Foo Bar@World" };

  it("puts saved rows first and new ones after in default order", () => {
    expect(arrange([a, b, tell], [channelKey(b)])).toEqual([b, a, tell]);
  });
  it("lets pins lead the saved order", () => {
    expect(arrange([a, b, tell], [channelKey(b), channelKey(a)], [channelKey(tell)])).toEqual([tell, b, a]);
  });
});

describe("pinFirst", () => {
  const say: ChannelRef = { kind: "custom", character: "A", id: "say" };
  const party: ChannelRef = { kind: "custom", character: "A", id: "party" };
  const tell: ChannelRef = { kind: "tell", character: "A", partner: "Foo Bar@World" };

  it("keeps the order without pins", () => {
    expect(pinFirst([party, say, tell], [])).toEqual([party, say, tell]);
  });
  it("puts pinned channels and tells first in pin order", () => {
    expect(pinFirst([party, say, tell], [channelKey(tell), channelKey(say)])).toEqual([tell, say, party]);
  });
  it("ignores pins for channels not in the list", () => {
    expect(pinFirst([party, say], ["x|B|say", channelKey(say)])).toEqual([say, party]);
  });
});
