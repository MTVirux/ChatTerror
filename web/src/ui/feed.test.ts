import { describe, expect, it } from "vitest";
import type { FeedItem } from "../core/accounts";
import { mergeFeed } from "./feed";

const it_ = (deviceId: string, id: string, ts: number, extra: Partial<FeedItem> = {}): FeedItem =>
  ({ deviceId, id, ts, channel: "say", sender: "A B", text: id, character: deviceId, outgoing: false, ...extra });

describe("mergeFeed", () => {
  it("keeps the same message id from two accounts apart and orders by time", () => {
    const out = mergeFeed([it_("a", "1", 1)], [it_("b", "1", 0), it_("a", "1", 1)], 10);
    expect(out.map((i) => `${i.deviceId}:${i.id}`)).toEqual(["b:1", "a:1"]);
  });
  it("caps to the newest", () => {
    expect(mergeFeed([], [it_("a", "1", 1), it_("a", "2", 2), it_("a", "3", 3)], 2).map((i) => i.id)).toEqual(["2", "3"]);
  });
});
