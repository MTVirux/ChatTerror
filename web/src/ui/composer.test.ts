import { describe, expect, it } from "vitest";
import type { CustomChannel } from "../core/channelPrefs";
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
  const social: CustomChannel = { id: "s", character: "Alpha Beta", name: "Social", channels: ["freeCompany", "tell"] };

  it("tells the partner from a partner row", () => {
    expect(composerTab(social, { key: "t|Alpha Beta|A B@W", kind: "partner", label: "A B", hash: false, partner: "A B@W" })).toEqual({ kind: "tell", character: "Alpha Beta", partner: "A B@W" });
  });

  it("sends only to a type row's chat type", () => {
    expect(composerTab(social, { key: "c|Alpha Beta|freeCompany", kind: "type", label: "fc", hash: true, channel: "freeCompany" })).toEqual({ kind: "custom", channels: ["freeCompany"], latest: "freeCompany" });
  });

  it("offers every type of the channel from the all row", () => {
    expect(composerTab(social, { key: "x|Alpha Beta|s", kind: "all", label: "all", hash: true }, "tell")).toEqual({ kind: "custom", channels: ["freeCompany", "tell"], latest: "tell" });
  });
});
