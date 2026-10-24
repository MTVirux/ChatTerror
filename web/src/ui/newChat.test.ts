import { describe, expect, it } from "vitest";
import type { TellContact } from "../core/protocol";
import { newChatPeople } from "./newChat";

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
