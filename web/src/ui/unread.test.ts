import { describe, expect, it } from "vitest";
import type { FeedItem } from "../core/accounts";
import { EMPTY_CHANNEL_PREFS, type ChannelPrefs } from "../core/channelPrefs";
import { characterUnread, createUnreadTracker, viewUnread } from "./unread";

function fakeManager() {
  let cb: ((items: FeedItem[]) => void) | undefined;
  return {
    onMessages(fn: (items: FeedItem[]) => void) {
      cb = fn;
      return () => { cb = undefined; };
    },
    push: (items: FeedItem[]) => cb?.(items),
  };
}

function item(deviceId: string, over: Partial<FeedItem> = {}): FeedItem {
  return { deviceId, id: Math.random().toString(), ts: 1, channel: "party", sender: "A B", senderWorld: "W", text: "x", character: "Alpha Beta", outgoing: false, ...over };
}

const party = "c|Alpha Beta|party";
const is = (key: string) => (k: string) => k === key;

describe("unread tracker", () => {
  it("counts incoming messages per channel and account", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([item("a"), item("a"), item("b")]);
    expect(t.count("a", is(party))).toBe(2);
    expect(t.count("a", (k) => k.startsWith("c|"))).toBe(2);
    expect(t.summary("a")).toEqual({ unread: true, tells: 0 });
    expect(t.summary("b").unread).toBe(true);
  });

  it("does not count the open channel or outgoing messages", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    t.setOpen({ deviceId: "a", includes: is(party) });
    m.push([item("a"), item("b", { outgoing: true })]);
    expect(t.count("a", is(party))).toBe(0);
    expect(t.summary("b").unread).toBe(false);
  });

  it("counts tells separately and clears a channel when opened", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([item("a", { channel: "tell", sender: "C D", senderWorld: "W" })]);
    expect(t.summary("a")).toEqual({ unread: true, tells: 1 });
    expect(t.total(["a"])).toEqual({ unread: true, tells: 1 });
    t.setOpen({ deviceId: "a", includes: is("t|Alpha Beta|C D@W") });
    expect(t.summary("a")).toEqual({ unread: false, tells: 0 });
  });

  it("clears every channel an open custom channel includes", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([item("a"), item("a", { channel: "say" }), item("a", { channel: "tell", sender: "C D", senderWorld: "W" })]);
    t.setOpen({ deviceId: "a", includes: (k: string) => k.startsWith("c|") });
    expect(t.count("a", (k) => k.startsWith("c|"))).toBe(0);
    m.push([item("a", { channel: "say" })]);
    expect(t.count("a", is("c|Alpha Beta|say"))).toBe(0);
    expect(t.summary("a")).toEqual({ unread: true, tells: 1 });
  });

  it("only totals the given accounts so removed ones drop out", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([item("gone", { channel: "tell", sender: "C D", senderWorld: "W" })]);
    expect(t.total(["gone"])).toEqual({ unread: true, tells: 1 });
    expect(t.total(["a"])).toEqual({ unread: false, tells: 0 });
  });

  it("notifies subscribers and stops listening on close", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    let calls = 0;
    t.subscribe(() => calls++);
    m.push([item("a")]);
    expect(calls).toBe(1);
    t.close();
    m.push([item("a")]);
    expect(t.count("a", is(party))).toBe(1);
  });

  it("leaves muted channels out of the summaries but keeps counting them", () => {
    const m = fakeManager();
    const tell = "t|Alpha Beta|C D@W";
    const muted = new Set([party, tell]);
    const t = createUnreadTracker(m, (deviceId, key) => deviceId === "a" && muted.has(key));
    m.push([item("a"), item("a", { channel: "tell", sender: "C D", senderWorld: "W" })]);
    expect(t.count("a", is(party))).toBe(1);
    expect(t.summary("a")).toEqual({ unread: false, tells: 0 });
    expect(t.total(["a"])).toEqual({ unread: false, tells: 0 });
    muted.delete(tell);
    expect(t.summary("a")).toEqual({ unread: true, tells: 1 });
  });

  it("summarizes one character of an account", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([item("a", { channel: "tell", sender: "C D", senderWorld: "W" }), item("a", { character: "Other One" })]);
    expect(t.summary("a", "Alpha Beta")).toEqual({ unread: true, tells: 1 });
    expect(t.summary("a", "Other One")).toEqual({ unread: true, tells: 0 });
    expect(t.summary("a", "Nobody")).toEqual({ unread: false, tells: 0 });
  });
});

describe("viewUnread", () => {
  const prefs: ChannelPrefs = {
    ...EMPTY_CHANNEL_PREFS,
    custom: [{ id: "s", character: "Alpha Beta", name: "Social", channels: ["party", "tell"] }],
    muted: ["t|Alpha Beta|E F@W"],
  };

  it("counts a view's unread and its tells, leaving muted ones out", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([
      item("a"),
      item("a", { channel: "tell", sender: "C D", senderWorld: "W" }),
      item("a", { channel: "tell", sender: "E F", senderWorld: "W" }),
      item("a", { channel: "say" }),
    ]);
    expect(viewUnread(t, "a", prefs, "x|Alpha Beta|s")).toEqual({ count: 2, tells: 1 });
    expect(viewUnread(t, "a", prefs, "t|Alpha Beta|E F@W")).toEqual({ count: 0, tells: 0 });
    expect(t.count("a", (k) => k === "t|Alpha Beta|E F@W")).toBe(1);
  });

  it("shows nothing for a muted channel", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([item("a"), item("a", { channel: "tell", sender: "C D", senderWorld: "W" })]);
    const muted = { ...prefs, muted: ["x|Alpha Beta|s"] };
    expect(viewUnread(t, "a", muted, "x|Alpha Beta|s")).toEqual({ count: 0, tells: 0 });
  });
});

describe("characterUnread", () => {
  const tell = (sender: string, over: Partial<FeedItem> = {}) => item("a", { channel: "tell", sender, senderWorld: "W", ...over });
  const withParty: ChannelPrefs = {
    ...EMPTY_CHANNEL_PREFS,
    custom: [{ id: "party", character: "Alpha Beta", name: "Party", channels: ["party"] }],
  };

  it("leaves out chat types no channel shows", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([item("a", { channel: "say" })]);
    expect(characterUnread(t, "a", EMPTY_CHANNEL_PREFS, "Alpha Beta")).toEqual({ unread: false, tells: 0 });
  });

  it("counts tells, which the Tells channel always shows", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([tell("C D")]);
    expect(characterUnread(t, "a", EMPTY_CHANNEL_PREFS, "Alpha Beta")).toEqual({ unread: true, tells: 1 });
  });

  it("counts a chat type one of the character's channels shows", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([item("a")]);
    expect(characterUnread(t, "a", withParty, "Alpha Beta")).toEqual({ unread: true, tells: 0 });
  });

  it("leaves out a muted partner's tells", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([tell("E F")]);
    const muted = { ...EMPTY_CHANNEL_PREFS, muted: ["t|Alpha Beta|E F@W"] };
    expect(characterUnread(t, "a", muted, "Alpha Beta")).toEqual({ unread: false, tells: 0 });
  });

  it("leaves out another character's messages", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    m.push([tell("C D", { character: "Other One" }), item("a", { character: "Other One" })]);
    expect(characterUnread(t, "a", withParty, "Alpha Beta")).toEqual({ unread: false, tells: 0 });
  });
});
