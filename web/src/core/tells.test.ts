import "fake-indexeddb/auto";
import { IDBFactory } from "fake-indexeddb";
import { describe, expect, it } from "vitest";
import { decode, encode } from "./b64url";
import { exportPublicRaw, generateTellKey, openTell, sealTell } from "./crypto";
import type { TellBody, TellContact } from "./protocol";
import { openAccountStore, resetStorageForTests } from "./storage";
import { buildCopies, findContact, openTellFrame, TELL_TTL_MS, tellToItem } from "./tells";

const contact: TellContact = { character: "Main Char", characterWorld: "Twintania", characterHash: "me", name: "Bob Smith", world: "Lich", hash: "bob", installId: "bob-install", key: "bob-key" };
const now = Date.now();
const body: TellBody = { id: "t1", fromHash: "bob", fromName: "Forged Name", fromWorld: "Lich", toHash: "me", toName: "Main Char", toWorld: "Twintania", text: "hi", ts: now - 5 };

describe("tells", () => {
  it("names incoming tells from the contact list, not the body", () => {
    expect(tellToItem(body, [contact], false, now)).toEqual({
      id: "t1", ts: now - 5, channel: "tell", sender: "Bob Smith", senderWorld: "Lich", text: "hi", character: "Main Char", outgoing: false,
    });
  });

  it("never dates a tell past the receiver's clock", () => {
    expect(tellToItem({ ...body, ts: now + 9_999_999 }, [contact], false, now)?.ts).toBe(now);
    expect(tellToItem({ ...body, ts: now - 1000 }, [contact], false, now)?.ts).toBe(now - 1000);
  });

  it("drops tells older than the relay keeps them, so a replay can't come back", () => {
    expect(tellToItem({ ...body, ts: now - TELL_TTL_MS - 1 }, [contact], false, now)).toBeNull();
    expect(tellToItem({ ...body, ts: now - TELL_TTL_MS + 1000 }, [contact], false, now)).not.toBeNull();
  });

  it("drops tells that would not make a valid chat item", () => {
    expect(tellToItem({ ...body, text: "x".repeat(5000) }, [contact], false, now)).toBeNull();
    const own = { ...body, fromHash: "me", fromName: "Main|Char" };
    expect(tellToItem(own, [contact], true, now)).toBeNull();
  });

  it("drops tells from unknown senders", () => {
    expect(tellToItem({ ...body, fromHash: "stranger" }, [contact], false, now)).toBeNull();
    expect(tellToItem({ ...body, toHash: "alt" }, [contact], false, now)).toBeNull();
  });

  it("turns own copies into outgoing items", () => {
    const own = { ...body, fromHash: "me", fromName: "Main Char", toHash: "bob", toName: "Bob Smith", toWorld: "Lich" };
    expect(tellToItem(own, [contact], true, now)).toMatchObject({ sender: "Bob Smith", senderWorld: "Lich", character: "Main Char", outgoing: true });
  });

  it("finds contacts by Name@World for the given character only", () => {
    const alt = { ...contact, character: "Alt Char", characterHash: "alt" };
    expect(findContact([contact, alt], "bob smith@lich", "Main Char")?.characterHash).toBe("me");
    expect(findContact([contact, alt], "Bob Smith@Lich", "Alt Char")?.characterHash).toBe("alt");
    expect(findContact([contact], "Bob Smith@Lich", "Alt Char")).toBeUndefined();
    expect(findContact([contact], "Bob Smith@Lich", undefined)).toBeUndefined();
    expect(findContact([contact], "Bob Smith@Twintania", "Main Char")).toBeUndefined();
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

describe("openTellFrame", () => {
  async function seed(contacts: TellContact[]) {
    resetStorageForTests();
    globalThis.indexedDB = new IDBFactory();
    const store = openAccountStore("chatterror-tells");
    const aesKey = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
    await store.setPairing({ deviceId: "dev", token: "t", aesKey, pluginPublicKey: "my-install", devicePublicKey: "d", fingerprint: "1 2" });
    const pair = await generateTellKey();
    const publicKey = encode(await exportPublicRaw(pair.publicKey));
    await store.setMeta("tellKey", { privateKey: pair.privateKey, publicKey });
    await store.setMeta("lastSettings", { type: "settings", seq: 1, relayChannels: [], sendChannels: [], maxLength: 500, contacts });
    const seal = async (tell: TellBody) => encode(await sealTell(decode(publicKey), new TextEncoder().encode(JSON.stringify(tell))));
    return { store, seal };
  }

  const own = { ...body, fromHash: "me", fromName: "Main Char", toHash: "bob", toName: "Bob Smith", toWorld: "Lich" };

  it("only accepts copies of our own tells from our own install", async () => {
    const { store, seal } = await seed([contact]);
    const envelope = await seal(own);
    expect(await openTellFrame(store, "my-install-id", "t1", envelope, "someone-else")).toBeNull();
    expect(await openTellFrame(store, "my-install-id", "t1", envelope, "my-install")).toMatchObject({ outgoing: true, character: "Main Char" });
  });

  it("does not take a friend's tell claiming to be from one of our characters as our own", async () => {
    const { store, seal } = await seed([contact]);
    expect(await openTellFrame(store, "bob-install", "t1", await seal(own), "bob-key")).toBeNull();
  });

  it("accepts a tell only from the paired install, key, character and recipient of a contact", async () => {
    const { store, seal } = await seed([contact]);
    const envelope = await seal(body);
    expect(await openTellFrame(store, "bob-install", "t1", envelope, "bob-key")).toMatchObject({ sender: "Bob Smith", outgoing: false });
    expect(await openTellFrame(store, "other-install", "t1", envelope, "bob-key")).toBeNull();
    expect(await openTellFrame(store, "bob-install", "t1", envelope, "other-key")).toBeNull();
    expect(await openTellFrame(store, "bob-install", "t1", await seal({ ...body, fromHash: "carol" }), "bob-key")).toBeNull();
    expect(await openTellFrame(store, "bob-install", "t1", await seal({ ...body, toHash: "alt" }), "bob-key")).toBeNull();
    expect(await openTellFrame(store, "bob-install", "t2", envelope, "bob-key")).toBeNull();
  });

  it("drops a tell whose body has a bad timestamp", async () => {
    const { store, seal } = await seed([contact]);
    for (const ts of [-Infinity, -1e20, NaN]) {
      expect(await openTellFrame(store, "bob-install", "t1", await seal({ ...body, ts }), "bob-key")).toBeNull();
    }
  });
});
