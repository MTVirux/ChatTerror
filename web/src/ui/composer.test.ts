import { describe, expect, it } from "vitest";
import type { ChatRow } from "./chatRows";
import { blockedReason, composerTab, pickable, preferredChannel, type Tab } from "./Composer";

describe("composer channel", () => {
  const tab: Tab = { kind: "custom", channels: ["freeCompany", "linkshell1", "tell"], latest: "linkshell1" };

  it("offers only the custom channel's sendable members", () => {
    expect(pickable(tab, ["party", "linkshell1", "freeCompany", "tell"])).toEqual(["linkshell1", "freeCompany", "tell"]);
  });

  it("defaults to the newest message's channel, then keeps the user's choice", () => {
    expect(preferredChannel(tab, ["freeCompany", "linkshell1"], undefined)).toBe("linkshell1");
    expect(preferredChannel(tab, ["freeCompany", "linkshell1"], "freeCompany")).toBe("freeCompany");
  });

  it("falls back to the first sendable chat channel", () => {
    expect(preferredChannel({ ...tab, latest: "party" }, ["tell", "freeCompany"], undefined)).toBe("freeCompany");
    expect(preferredChannel(tab, ["party"], undefined)).toBeUndefined();
  });
});

describe("composer blocking", () => {
  const contact = { character: "Me", characterWorld: "Lich", characterHash: "me", name: "Bob Smith", world: "Lich", hash: "bob", installId: "bob-install", key: "bob-key" };

  it("lets tells to ChatTerror friends through while the game is offline", () => {
    expect(blockedReason("gameOffline", [contact], "tell", "Bob Smith@Lich", "Me")).toBeNull();
  });

  it("only relays from the conversation's own character", () => {
    expect(blockedReason("gameOffline", [contact], "tell", "Bob Smith@Lich", "My Alt")).not.toBeNull();
    expect(blockedReason("gameOffline", [contact], "tell", "Bob Smith@Lich", undefined)).not.toBeNull();
  });

  it("blocks everything else while the game is offline", () => {
    expect(blockedReason("gameOffline", [contact], "tell", "Cid Garlond@Lich")).not.toBeNull();
    expect(blockedReason("gameOffline", [contact], "party", undefined)).not.toBeNull();
    expect(blockedReason("relayOffline", [contact], "tell", "Bob Smith@Lich", "Me")).not.toBeNull();
  });
});

describe("composerTab", () => {
  const group = { key: "x|Alpha Beta|s", kind: "group", character: "Alpha Beta", name: "Social", custom: { id: "s", character: "Alpha Beta", name: "Social", channels: ["freeCompany", "linkshell1"] }, pinned: false, muted: false } as ChatRow;
  const dm = { key: "t|Alpha Beta|A B@W", kind: "dm", character: "Alpha Beta", name: "A B", partner: "A B@W", world: "W", pinned: false, muted: false } as ChatRow;

  it("tells the partner in a DM", () => {
    expect(composerTab(dm)).toEqual({ kind: "tell", character: "Alpha Beta", partner: "A B@W" });
  });

  it("offers the group's channels, defaulting to the newest message's", () => {
    expect(composerTab(group, "linkshell1")).toEqual({ kind: "custom", channels: ["freeCompany", "linkshell1"], latest: "linkshell1" });
  });
});
