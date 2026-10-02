import { describe, expect, it } from "vitest";
import { blockedReason, pickable, preferredChannel, type Tab } from "./Composer";

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
  const contact = { character: "Me", characterWorld: "Lich", characterHash: "me", name: "Bob Smith", world: "Lich", hash: "bob" };

  it("lets tells to ChatTerror friends through while the game is offline", () => {
    expect(blockedReason("gameOffline", [contact], "tell", "Bob Smith@Lich")).toBeNull();
  });

  it("blocks everything else while the game is offline", () => {
    expect(blockedReason("gameOffline", [contact], "tell", "Cid Garlond@Lich")).not.toBeNull();
    expect(blockedReason("gameOffline", [contact], "party", undefined)).not.toBeNull();
    expect(blockedReason("relayOffline", [contact], "tell", "Bob Smith@Lich")).not.toBeNull();
  });
});
