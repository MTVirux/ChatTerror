import "fake-indexeddb/auto";
import { IDBFactory } from "fake-indexeddb";
import { beforeEach, describe, expect, it } from "vitest";
import type { ChatItem } from "./protocol";
import { openAccountStore, resetStorageForTests, type AccountStore } from "./storage";

function item(id: string, ts: number): ChatItem {
  return { id, ts, channel: "say", sender: "A B", text: id, character: "C D", outgoing: false };
}

let store: AccountStore;

beforeEach(() => {
  resetStorageForTests();
  globalThis.indexedDB = new IDBFactory();
  store = openAccountStore("chatterror-test");
});

describe("storage", () => {
  it("returns only newly added messages", async () => {
    expect((await store.addMessages([item("a", 1), item("b", 2)], 10)).map((m) => m.id)).toEqual(["a", "b"]);
    expect((await store.addMessages([item("b", 2), item("c", 3), item("c", 3)], 10)).map((m) => m.id)).toEqual(["c"]);
  });

  it("trims to the limit keeping the newest", async () => {
    await store.addMessages([item("a", 1), item("b", 2), item("c", 3), item("d", 4)], 10);
    await store.addMessages([item("e", 5)], 3);
    expect((await store.loadMessages(100)).map((m) => m.id)).toEqual(["c", "d", "e"]);
    await store.trimMessages(1);
    expect((await store.loadMessages(100)).map((m) => m.id)).toEqual(["e"]);
  });

  it("loads the newest messages oldest first", async () => {
    await store.addMessages([item("c", 3), item("a", 1), item("b", 2)], 10);
    expect((await store.loadMessages(2)).map((m) => m.id)).toEqual(["b", "c"]);
  });

  it("skips malformed messages stored before validation", async () => {
    await store.addMessages([item("a", 1), item("bad", 1e300), { ...item("odd", 2), character: 5 as unknown as string }], 10);
    expect((await store.loadMessages(10)).map((m) => m.id)).toEqual(["a"]);
  });

  it("clears messages", async () => {
    await store.addMessages([item("a", 1)], 10);
    await store.clearMessages();
    expect(await store.loadMessages(10)).toEqual([]);
  });

  it("round trips meta with defaults", async () => {
    expect(await store.getMeta("lastSeenWs")).toBe(0);
    expect(await store.getMeta("mutedChannels")).toEqual([]);
    await store.setMeta("lastSeenWs", 42);
    await store.setMeta("mutedChannels", ["say", "yell"]);
    expect(await store.getMeta("lastSeenWs")).toBe(42);
    expect(await store.getMeta("mutedChannels")).toEqual(["say", "yell"]);
  });

  it("stores the pairing with a CryptoKey and wipes everything", async () => {
    const aesKey = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
    await store.setPairing({ deviceId: "dev", token: "d.dev.s", aesKey, pluginPublicKey: "p", devicePublicKey: "d", fingerprint: "123 456" });
    await store.setMeta("lastSeenPush", 7);
    await store.addMessages([item("a", 1)], 10);

    const pairing = await store.getPairing();
    expect(pairing?.deviceId).toBe("dev");
    expect(pairing?.aesKey).toBeInstanceOf(CryptoKey);

    await store.wipe();
    expect(await store.getPairing()).toBeUndefined();
    expect(await store.getMeta("lastSeenPush")).toBe(0);
    expect(await store.loadMessages(10)).toEqual([]);
  });

  it("keeps accounts apart", async () => {
    const other = openAccountStore("chatterror-other");
    await store.addMessages([item("a", 1)], 10);
    await store.setMeta("lastSeenWs", 5);
    expect(await other.loadMessages(10)).toEqual([]);
    expect(await other.getMeta("lastSeenWs")).toBe(0);
  });

  it("deletes the database even while another connection is open", async () => {
    await store.addMessages([item("a", 1)], 10);
    await store.destroy();
    const reopened = openAccountStore("chatterror-test");
    expect(await reopened.loadMessages(10)).toEqual([]);
  });
});
