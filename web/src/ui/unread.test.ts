import { describe, expect, it } from "vitest";
import type { FeedItem } from "../core/accounts";
import { createUnreadTracker } from "./unread";

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

  it("counts nothing while Home is open", () => {
    const m = fakeManager();
    const t = createUnreadTracker(m);
    t.setOpen("home");
    m.push([item("a")]);
    expect(t.total(["a"]).unread).toBe(false);
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
});
