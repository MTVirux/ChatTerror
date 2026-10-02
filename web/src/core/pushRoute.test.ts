import "fake-indexeddb/auto";
import { IDBFactory } from "fake-indexeddb";
import { beforeEach, describe, expect, it } from "vitest";
import { decode, encode } from "./b64url";
import { exportPublicRaw, generateTellKey, sealPayload, sealTell } from "./crypto";
import type { ChatItem } from "./protocol";
import { notificationTitle, routePush } from "./pushRoute";
import { addAccount, resetRegistryForTests, updateAccount } from "./registry";
import { LEGACY_DB_NAME, openAccountStore, resetStorageForTests } from "./storage";
import { TELL_TTL_MS } from "./tells";

function newKey(): Promise<CryptoKey> {
  return crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
}

async function seed(deviceId: string) {
  const dbName = `chatterror-${deviceId}`;
  const aesKey = await newKey();
  await openAccountStore(dbName).setPairing({ deviceId, token: "t", aesKey, pluginPublicKey: `pk-${deviceId}`, devicePublicKey: "d", fingerprint: "1 2" });
  await addAccount({ deviceId, dbName, pluginPublicKey: `pk-${deviceId}`, label: "", addedAt: 1, status: "active" });
  return aesKey;
}

function item(id: string): ChatItem {
  return { id, ts: 100, channel: "say", sender: "Y'shtola Rhul", text: "hi", character: "Me", outgoing: false };
}

function seal(key: CryptoKey, chat: ChatItem, seq: number): Promise<string> {
  return sealPayload(key, "p2d", { type: "chat", seq, item: chat });
}

beforeEach(() => {
  resetStorageForTests();
  resetRegistryForTests();
  globalThis.indexedDB = new IDBFactory();
});

async function expectRoutedToB(body: unknown) {
  const routed = await routePush(body);
  expect(routed).toMatchObject({ deviceId: "b", multiple: true, label: "Account 2", item: { id: "m1" } });
  expect((await openAccountStore("chatterror-b").loadMessages(10)).map((i) => i.id)).toEqual(["m1"]);
  expect(await openAccountStore("chatterror-a").loadMessages(10)).toEqual([]);
  expect(await openAccountStore("chatterror-b").getMeta("lastSeenPush")).toBe(5);
}

describe("routePush", () => {
  it("routes by device id", async () => {
    await seed("a");
    const keyB = await seed("b");
    await expectRoutedToB({ p: await seal(keyB, item("m1"), 5), d: "b" });
  });

  it("falls back to each key without a device id", async () => {
    await seed("a");
    const keyB = await seed("b");
    await expectRoutedToB({ p: await seal(keyB, item("m1"), 5) });
  });

  it("falls back when the device id is unknown", async () => {
    await seed("a");
    const keyB = await seed("b");
    await expectRoutedToB({ p: await seal(keyB, item("m1"), 5), d: "zzz" });
  });

  it("works for the adopted legacy database", async () => {
    const aesKey = await newKey();
    const legacy = openAccountStore(LEGACY_DB_NAME);
    await legacy.setPairing({ deviceId: "old", token: "t", aesKey, pluginPublicKey: "pk-old", devicePublicKey: "d", fingerprint: "1 2" });
    const routed = await routePush({ p: await seal(aesKey, item("m1"), 5), d: "old" });
    expect(routed).toMatchObject({ deviceId: "old", multiple: false, item: { id: "m1" } });
    expect((await legacy.loadMessages(10)).map((i) => i.id)).toEqual(["m1"]);
  });

  it("uses the custom name in the label", async () => {
    await seed("a");
    const keyB = await seed("b");
    await updateAccount("b", { name: "Main" });
    expect(await routePush({ p: await seal(keyB, item("m1"), 5), d: "b" })).toMatchObject({ deviceId: "b", label: "Main" });
  });

  it("numbers a label another account already uses", async () => {
    await seed("a");
    const keyB = await seed("b");
    await updateAccount("a", { label: "Alpha" });
    await updateAccount("b", { label: "Alpha" });
    expect(await routePush({ p: await seal(keyB, item("m1"), 5), d: "b" })).toMatchObject({ deviceId: "b", label: "Alpha (2)" });
  });

  it("drops replays", async () => {
    const key = await seed("a");
    const body = { p: await seal(key, item("m1"), 5), d: "a" };
    expect(await routePush(body)).not.toBeNull();
    expect(await routePush(body)).toBeNull();
  });

  it("returns null when nothing decrypts", async () => {
    await seed("a");
    expect(await routePush({ p: await seal(await newKey(), item("m1"), 5), d: "a" })).toBeNull();
  });
});

describe("notificationTitle", () => {
  it("uses a single-account title without the label", () => {
    const chat = item("m1");
    expect(notificationTitle({ deviceId: "a", label: "Alpha Beta", multiple: false, item: chat })).toBe("Y'shtola Rhul (Say)");
    expect(notificationTitle({ deviceId: "a", label: "Alpha Beta", multiple: true, item: chat })).toBe("Alpha Beta - Y'shtola Rhul (Say)");
  });
});

describe("routePush for relayed tells", () => {
  const contact = { character: "Me", characterWorld: "Lich", characterHash: "me", name: "Bob Smith", world: "Lich", hash: "bob" };

  async function tellAccount(deviceId: string) {
    const store = openAccountStore(`chatterror-${deviceId}`);
    const pair = await generateTellKey();
    const publicKey = encode(await exportPublicRaw(pair.publicKey));
    await store.setMeta("tellKey", { privateKey: pair.privateKey, publicKey });
    await store.setMeta("lastSettings", { type: "settings", seq: 1, relayChannels: [], sendChannels: [], maxLength: 500, contacts: [contact] });
    const push = async (ts = Date.now()) => {
      const body = { id: "t1", fromHash: "bob", fromName: "Bob Smith", fromWorld: "Lich", toHash: "me", toName: "Me", toWorld: "Lich", text: "hi", ts };
      const envelope = encode(await sealTell(decode(publicKey), new TextEncoder().encode(JSON.stringify(body))));
      return { t: "tell", i: "t1", f: "bob", e: envelope, k: "bob-install", d: deviceId };
    };
    return { store, push };
  }

  it("opens a tell with the account's tell key and stores it", async () => {
    await seed("a");
    await seed("b");
    const { store, push } = await tellAccount("b");

    const routed = await routePush(await push());

    expect(routed).toMatchObject({ deviceId: "b", item: { id: "t1", sender: "Bob Smith", channel: "tell" } });
    expect((await store.loadMessages(10)).map((i) => i.id)).toEqual(["t1"]);
  });

  it("numbers a label another account already uses", async () => {
    await seed("a");
    await seed("b");
    await updateAccount("a", { label: "Alpha" });
    await updateAccount("b", { label: "Alpha" });
    const { push } = await tellAccount("b");
    expect(await routePush(await push())).toMatchObject({ deviceId: "b", label: "Alpha (2)" });
  });

  it("does not notify again for a tell it already stored", async () => {
    await seed("b");
    const { push } = await tellAccount("b");
    const body = await push();
    expect(await routePush(body)).toMatchObject({ item: { id: "t1" } });
    expect(await routePush(body)).toBe("duplicate");
  });

  it("drops a tell older than the relay keeps tells", async () => {
    await seed("b");
    const { store, push } = await tellAccount("b");
    expect(await routePush(await push(Date.now() - TELL_TTL_MS - 1000))).toBeNull();
    expect(await store.loadMessages(10)).toEqual([]);
  });

  it("returns null for a tell it can't open", async () => {
    await seed("b");
    expect(await routePush({ t: "tell", i: "t1", f: "bob", e: "garbage", k: "bob-install", d: "b" })).toBeNull();
  });
});
