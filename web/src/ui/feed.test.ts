import { describe, expect, it } from "vitest";
import type { FeedItem } from "../core/accounts";
import { mergeFeed } from "./feed";

const it_ = (deviceId: string, id: string, ts: number, extra: Partial<FeedItem> = {}): FeedItem =>
  ({ deviceId, id, ts, channel: "say", sender: "A B", text: id, character: deviceId, outgoing: false, ...extra });

describe("mergeFeed", () => {
  it("keeps the same message id from two accounts apart and orders by time", () => {
    const out = mergeFeed([it_("a", "1", 1)], [it_("b", "1", 0), it_("a", "1", 1)], 10, () => "all");
    expect(out.map((i) => `${i.deviceId}:${i.id}`)).toEqual(["b:1", "a:1"]);
  });
  it("caps to the newest", () => {
    expect(mergeFeed([], [it_("a", "1", 1), it_("a", "2", 2), it_("a", "3", 3)], 2, () => "all").map((i) => i.id)).toEqual(["2", "3"]);
  });
  it("caps each chat on its own", () => {
    const quiet = it_("a", "q1", 0, { channel: "party" });
    const busy = [it_("a", "b1", 1), it_("a", "b2", 2), it_("a", "b3", 3)];
    expect(mergeFeed([quiet], busy, 2, (i) => i.channel).map((i) => i.id)).toEqual(["q1", "b2", "b3"]);
  });
  it("caps the whole feed to the newest overall", () => {
    const party = (id: string, ts: number) => it_("a", id, ts, { channel: "party" });
    const yell = (id: string, ts: number) => it_("a", id, ts, { channel: "yell" });
    const items = [it_("a", "s1", 1), party("p1", 2), yell("y1", 3), it_("a", "s2", 4), party("p2", 5), yell("y2", 6)];
    expect(mergeFeed([], items, 2, (i) => i.channel, 4).map((i) => i.id)).toEqual(["y1", "s2", "p2", "y2"]);
  });
  it("returns the same list when nothing is new", () => {
    const existing = [it_("a", "1", 1)];
    expect(mergeFeed(existing, [it_("a", "1", 1)], 10, () => "all")).toBe(existing);
  });
});
