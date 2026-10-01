import { describe, expect, it } from "vitest";
import type { AccountView, FeedItem } from "../core/accounts";
import { defaultSendAccount, mergeFeed } from "./feed";

const it_ = (deviceId: string, id: string, ts: number, extra: Partial<FeedItem> = {}): FeedItem =>
  ({ deviceId, id, ts, channel: "say", sender: "A B", text: id, character: deviceId, outgoing: false, ...extra });
const acct = (deviceId: string, status: string) => ({ deviceId, label: deviceId, status: "active", unread: 0, state: { status } }) as unknown as AccountView;

describe("mergeFeed", () => {
  it("keeps the same message id from two accounts apart and orders by time", () => {
    const out = mergeFeed([it_("a", "1", 1)], [it_("b", "1", 0), it_("a", "1", 1)], 10);
    expect(out.map((i) => `${i.deviceId}:${i.id}`)).toEqual(["b:1", "a:1"]);
  });
  it("caps to the newest", () => {
    expect(mergeFeed([], [it_("a", "1", 1), it_("a", "2", 2), it_("a", "3", 3)], 2).map((i) => i.id)).toEqual(["2", "3"]);
  });
});

describe("defaultSendAccount", () => {
  const accounts = [acct("a", "online"), acct("b", "online")];
  it("picks the account of the newest line with the tell partner", () => {
    const items = [it_("a", "1", 1, { channel: "tell", sender: "X Y@W" }), it_("b", "2", 2, { channel: "tell", sender: "X Y@W" })];
    expect(defaultSendAccount(items, accounts, "a", "X Y@W")).toBe("b");
  });
  it("keeps the current choice while it is online", () => {
    expect(defaultSendAccount([], accounts, "b")).toBe("b");
  });
  it("keeps a manual choice in a tell tab when no partner is passed", () => {
    const items = [it_("b", "1", 1, { channel: "tell", sender: "X Y@W" })];
    expect(defaultSendAccount(items, accounts, "a")).toBe("a");
  });
  it("falls back to the first online account", () => {
    expect(defaultSendAccount([], [acct("a", "gameOffline"), acct("b", "online")], undefined)).toBe("b");
    expect(defaultSendAccount([], [acct("a", "gameOffline")], undefined)).toBe("a");
  });
});
