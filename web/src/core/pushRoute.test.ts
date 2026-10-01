import "fake-indexeddb/auto";
import { IDBFactory } from "fake-indexeddb";
import { beforeEach, describe, expect, it } from "vitest";
import { sealPayload } from "./crypto";
import type { ChatItem } from "./protocol";
import { notificationTitle, routePush } from "./pushRoute";
import { addAccount, resetRegistryForTests } from "./registry";
import { LEGACY_DB_NAME, openAccountStore, resetStorageForTests } from "./storage";

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
