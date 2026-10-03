import { describe, expect, it } from "vitest";
import type { AccountView } from "../core/accounts";
import { characterGroups, describeCharacter } from "./characters";

const acct = (deviceId: string, character?: string, status = "online", accountStatus = "active") =>
  ({ deviceId, label: `Client ${deviceId}`, character, characterWorld: character ? "Twintania" : undefined, status: accountStatus, unread: 0, state: { status } }) as unknown as AccountView;

describe("describeCharacter", () => {
  it("gives the logged-in character its world and live status", () => {
    expect(describeCharacter(acct("a", "Mira Vale"), "Mira Vale")).toEqual({ name: "Mira Vale", world: "Twintania", status: "Online", online: true });
    expect(describeCharacter(acct("a", "Mira Vale", "gameOffline"), "Mira Vale")).toEqual({ name: "Mira Vale", world: "Twintania", status: "Game offline", online: false });
  });

  it("marks alts as not logged in", () => {
    expect(describeCharacter(acct("a", "Mira Vale"), "Kett Orin")).toEqual({ name: "Kett Orin", status: "Not logged in", online: false });
  });

  it("names pending, removed and empty clients by their label", () => {
    expect(describeCharacter(acct("a", undefined, "pending"), null)).toEqual({ name: "Client a", status: "Waiting for approval", online: false });
    expect(describeCharacter(acct("a", "Mira Vale", "revoked", "revoked"), "Mira Vale")).toEqual({ name: "Client a", status: "Removed", online: false });
    expect(describeCharacter(acct("a"), null)).toEqual({ name: "Client a", status: "No characters yet", online: false });
  });
});

describe("characterGroups", () => {
  it("lists each client's characters with their unread, and pending clients as one row", () => {
    const groups = characterGroups(
      [acct("a", "Mira Vale"), acct("b", undefined, "pending")],
      (a) => (a.deviceId === "a" ? ["Mira Vale", "Kett Orin"] : ["Never Asked"]),
      (_deviceId, character) => ({ unread: character === "Kett Orin", tells: character === "Kett Orin" ? 2 : 0 }),
    );
    expect(groups.map((g) => [g.deviceId, g.label])).toEqual([["a", "Client a"], ["b", "Client b"]]);
    expect(groups[0].options.map((o) => [o.place.character, o.status, o.tells, o.unread])).toEqual([
      ["Mira Vale", "Online", 0, false],
      ["Kett Orin", "Not logged in", 2, true],
    ]);
    expect(groups[1].options).toEqual([{ place: { deviceId: "b", character: null }, name: "Client b", status: "Waiting for approval", online: false, tells: 0, unread: false }]);
  });

  it("gives a client without characters one row", () => {
    const [group] = characterGroups([acct("a")], () => [], () => ({ unread: false, tells: 0 }));
    expect(group.options.map((o) => [o.place, o.status])).toEqual([[{ deviceId: "a", character: null }, "No characters yet"]]);
  });
});
