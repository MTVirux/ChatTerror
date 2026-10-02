import { describe, expect, it } from "vitest";
import { decode, encode } from "./b64url";
import { exportPublicRaw, generateTellKey, openTell } from "./crypto";
import type { TellBody, TellContact } from "./protocol";
import { buildCopies, findContact, tellToItem } from "./tells";

const contact: TellContact = { character: "Main Char", characterWorld: "Twintania", characterHash: "me", name: "Bob Smith", world: "Lich", hash: "bob" };
const body: TellBody = { id: "t1", fromHash: "bob", fromName: "Forged Name", fromWorld: "Lich", toHash: "me", toName: "Main Char", toWorld: "Twintania", text: "hi", ts: 5 };

describe("tells", () => {
  it("names incoming tells from the contact list, not the body", () => {
    expect(tellToItem(body, "bob", [contact])).toEqual({
      id: "t1", ts: 5, channel: "tell", sender: "Bob Smith", senderWorld: "Lich", text: "hi", character: "Main Char", outgoing: false,
    });
  });

  it("never dates a tell past the receiver's clock", () => {
    expect(tellToItem({ ...body, ts: 9_999_999 }, "bob", [contact], 5000)?.ts).toBe(5000);
    expect(tellToItem({ ...body, ts: 1000 }, "bob", [contact], 5000)?.ts).toBe(1000);
  });

  it("drops tells from unknown senders", () => {
    expect(tellToItem(body, "stranger", [contact])).toBeNull();
  });

  it("turns own copies into outgoing items", () => {
    const own = { ...body, fromHash: "me", fromName: "Main Char", toHash: "bob", toName: "Bob Smith", toWorld: "Lich" };
    expect(tellToItem(own, "me", [contact])).toMatchObject({ sender: "Bob Smith", senderWorld: "Lich", character: "Main Char", outgoing: true });
  });

  it("finds contacts by Name@World, preferring a character", () => {
    const alt = { ...contact, character: "Alt Char", characterHash: "alt" };
    expect(findContact([contact, alt], "bob smith@lich")?.characterHash).toBe("me");
    expect(findContact([contact, alt], "Bob Smith@Lich", "Alt Char")?.characterHash).toBe("alt");
    expect(findContact([contact], "Bob Smith@Twintania")).toBeUndefined();
  });

  it("seals one copy per key and skips the sending device", async () => {
    const theirs = await generateTellKey();
    const mine = await generateTellKey();
    const theirsRaw = await exportPublicRaw(theirs.publicKey);
    const mineRaw = await exportPublicRaw(mine.publicKey);
    const recipient = { installPublicKey: "x", issuedAt: 1, entries: [{ target: "plugin", key: encode(theirsRaw), push: false }] };
    const own = {
      installPublicKey: "y",
      issuedAt: 1,
      entries: [
        { target: "plugin", key: encode(mineRaw), push: false },
        { target: "dev1", key: encode(mineRaw), push: true },
      ],
    };

    const copies = await buildCopies(body, recipient, own, "dev1");

    expect(copies.map((c) => [c.self, c.target])).toEqual([[false, "plugin"], [true, "plugin"]]);
    const opened = await openTell(theirs.privateKey, theirsRaw, decode(copies[0].envelope));
    expect(JSON.parse(new TextDecoder().decode(opened)).text).toBe("hi");
  });
});
