import { describe, expect, it } from "vitest";
import type { ChatItem } from "../core/protocol";
import { buildChannelTree, channelKey, channelSlug, firstChannel, inChannel, isCollapsed, itemChannelRef, parseChannelKey, pinFirst, toggleCategory, type ChannelRef } from "./channels";

function msg(over: Partial<ChatItem>): ChatItem {
  return { id: Math.random().toString(), ts: 1, channel: "party", sender: "Y'shtola Rhul", senderWorld: "Twintania", text: "hi", character: "Alpha Beta", outgoing: false, ...over };
}

describe("channel keys", () => {
  it("round trips chat and tell refs", () => {
    const chat = { kind: "chat", character: "Alpha Beta", channel: "freeCompany" } as const;
    const tell = { kind: "tell", character: "Alpha Beta", partner: "Thancred Waters@Gilgamesh" } as const;
    expect(parseChannelKey(channelKey(chat))).toEqual(chat);
    expect(parseChannelKey(channelKey(tell))).toEqual(tell);
    expect(parseChannelKey("nonsense")).toBeNull();
  });

  it("puts both directions of a tell in one channel", () => {
    const incoming = msg({ channel: "tell", sender: "Thancred Waters", senderWorld: "Gilgamesh" });
    const outgoing = msg({ channel: "tell", sender: "Thancred Waters@Gilgamesh", senderWorld: undefined, outgoing: true });
    expect(channelKey(itemChannelRef(incoming))).toBe(channelKey(itemChannelRef(outgoing)));
    expect(inChannel(outgoing, itemChannelRef(incoming))).toBe(true);
  });

  it("keeps a tell partner with an unknown world", () => {
    const item = msg({ channel: "tell", sender: "Thancred Waters", senderWorld: undefined });
    expect(itemChannelRef(item)).toEqual({ kind: "tell", character: "Alpha Beta", partner: "Thancred Waters" });
  });

  it("slugs channel labels", () => {
    expect(channelSlug("freeCompany")).toBe("fc");
    expect(channelSlug("party")).toBe("party");
  });
});

describe("buildChannelTree", () => {
  it("shows the logged-in character first, hiding relayed channels without messages by default", () => {
    const tree = buildChannelTree([msg({ character: "Other One", ts: 50 })], { character: "Alpha Beta", relayChannels: ["freeCompany", "party"] });
    expect(tree.map((c) => [c.character, c.active])).toEqual([["Alpha Beta", true], ["Other One", false]]);
    expect(tree[0].chats).toEqual([]);
  });

  it("shows relayed channels without messages when asked", () => {
    const tree = buildChannelTree([msg({ character: "Other One", ts: 50 })], { character: "Alpha Beta", relayChannels: ["freeCompany", "party"], showEmpty: true });
    expect(tree.map((c) => [c.character, c.active])).toEqual([["Alpha Beta", true], ["Other One", false]]);
    expect(tree[0].chats).toEqual(["freeCompany", "party"]);
  });

  it("adds seen chat types that are not relayed anymore after the relayed ones", () => {
    const tree = buildChannelTree([msg({ channel: "say" })], { character: "Alpha Beta", relayChannels: ["party"], showEmpty: true });
    expect(tree[0].chats).toEqual(["party", "say"]);
  });

  it("lists only seen chat types for other characters, in plugin order", () => {
    const items = [msg({ character: "Other One", channel: "say" }), msg({ character: "Other One", channel: "freeCompany" })];
    const tree = buildChannelTree(items, { character: "Alpha Beta", relayChannels: ["freeCompany", "say"] });
    expect(tree[1].chats).toEqual(["freeCompany", "say"]);
  });

  it("orders other characters by latest activity and tells newest first", () => {
    const items = [
      msg({ character: "Old Char", ts: 10 }),
      msg({ character: "New Char", ts: 20 }),
      msg({ character: "New Char", channel: "tell", sender: "A B", senderWorld: "W", ts: 21 }),
      msg({ character: "New Char", channel: "tell", sender: "C D", senderWorld: "W", ts: 30 }),
    ];
    const tree = buildChannelTree(items, { relayChannels: [] });
    expect(tree.map((c) => c.character)).toEqual(["New Char", "Old Char"]);
    expect(tree[0].tells.map((t) => t.partner)).toEqual(["C D@W", "A B@W"]);
  });

  it("finds the first channel", () => {
    const tree = buildChannelTree([], { character: "Alpha Beta", relayChannels: ["party"], showEmpty: true });
    expect(firstChannel(tree)).toEqual({ kind: "chat", character: "Alpha Beta", channel: "party" });
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

describe("pinFirst", () => {
  const say: ChannelRef = { kind: "chat", character: "A", channel: "say" };
  const party: ChannelRef = { kind: "chat", character: "A", channel: "party" };
  const tell: ChannelRef = { kind: "tell", character: "A", partner: "Foo Bar@World" };

  it("keeps the order without pins", () => {
    expect(pinFirst([party, say, tell], [])).toEqual([party, say, tell]);
  });
  it("puts pinned chats and tells first in pin order", () => {
    expect(pinFirst([party, say, tell], [channelKey(tell), channelKey(say)])).toEqual([tell, say, party]);
  });
  it("ignores pins for channels not in the list", () => {
    expect(pinFirst([party, say], ["c|B|say", channelKey(say)])).toEqual([say, party]);
  });
});
