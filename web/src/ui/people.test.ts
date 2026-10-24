import { describe, expect, it } from "vitest";
import type { TellContact } from "../core/protocol";
import { gameSpelling, matchPerson, newChatPeople } from "./people";

const contact = (name: string, world: string, character = "Alpha Beta"): TellContact =>
  ({ character, characterWorld: "Lich", characterHash: "h", name, world, hash: name, installId: "i", key: "k" });

describe("newChatPeople", () => {
  it("lists this character's ChatTerror friends by name and recent partners that aren't friends", () => {
    const out = newChatPeople(
      [contact("Yuna Tal", "Twintania"), contact("Kael Vey", "Phoenix"), contact("Sen Na", "Odin", "Other One")],
      ["Mira Sol@Lich", "yuna tal@Twintania", "Thancred Waters"],
      "Alpha Beta",
    );
    expect(out.friends.map((p) => p.partner)).toEqual(["Kael Vey@Phoenix", "Yuna Tal@Twintania"]);
    expect(out.recent).toEqual([{ partner: "Mira Sol@Lich", name: "Mira Sol", world: "Lich" }, { partner: "Thancred Waters", name: "Thancred Waters", world: undefined }]);
  });
});

describe("matchPerson", () => {
  const people = [{ partner: "Yuna Tal@Twintania", name: "Yuna Tal", world: "Twintania" }];

  it("opens a listed person's DM however the name was typed", () => {
    expect(matchPerson(people, "yuna tal@twintania")).toBe("Yuna Tal@Twintania");
  });

  it("spells a name nobody listed the way the game does", () => {
    expect(matchPerson(people, "Mira Sol@Lich")).toBe("Mira Sol@Lich");
    expect(matchPerson(people, "kai rho@phoenix")).toBe("Kai Rho@Phoenix");
  });
});

describe("gameSpelling", () => {
  it("capitalises each name part and the world, keeping apostrophes and hyphens", () => {
    expect(gameSpelling("y'shtola rhul@twintania")).toBe("Y'shtola Rhul@Twintania");
    expect(gameSpelling("kai-li rho@PHOENIX")).toBe("Kai-li Rho@Phoenix");
    expect(gameSpelling("Kai Rho@Phoenix")).toBe("Kai Rho@Phoenix");
  });
});
