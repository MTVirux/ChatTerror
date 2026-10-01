import "fake-indexeddb/auto";
import { beforeEach, describe, expect, it } from "vitest";
import type { ChatItem } from "./protocol";
import { addMessages, clearMessages, getMeta, getPairing, loadMessages, newestTs, setMeta, setPairing, trimMessages, wipeAll } from "./storage";

function item(id: string, ts: number): ChatItem {
  return { id, ts, channel: "say", sender: "A B", text: id, character: "C D", outgoing: false };
}

beforeEach(async () => {
  await wipeAll();
});

describe("storage", () => {
  it("returns only newly added messages", async () => {
    expect((await addMessages([item("a", 1), item("b", 2)], 10)).map((m) => m.id)).toEqual(["a", "b"]);
    expect((await addMessages([item("b", 2), item("c", 3), item("c", 3)], 10)).map((m) => m.id)).toEqual(["c"]);
  });

  it("trims to the limit keeping the newest", async () => {
    await addMessages([item("a", 1), item("b", 2), item("c", 3), item("d", 4)], 10);
    await addMessages([item("e", 5)], 3);
    expect((await loadMessages(100)).map((m) => m.id)).toEqual(["c", "d", "e"]);
    await trimMessages(1);
    expect((await loadMessages(100)).map((m) => m.id)).toEqual(["e"]);
  });

  it("loads the newest messages oldest first", async () => {
    await addMessages([item("c", 3), item("a", 1), item("b", 2)], 10);
    expect((await loadMessages(2)).map((m) => m.id)).toEqual(["b", "c"]);
    expect(await newestTs()).toBe(3);
  });

  it("clears messages", async () => {
    await addMessages([item("a", 1)], 10);
    await clearMessages();
    expect(await loadMessages(10)).toEqual([]);
    expect(await newestTs()).toBe(0);
  });

  it("round trips meta with defaults", async () => {
    expect(await getMeta("cacheLimit")).toBe(2000);
    expect(await getMeta("lastSeenWs")).toBe(0);
    expect(await getMeta("mutedChannels")).toEqual([]);
    await setMeta("lastSeenWs", 42);
    await setMeta("mutedChannels", ["say", "yell"]);
    expect(await getMeta("lastSeenWs")).toBe(42);
    expect(await getMeta("mutedChannels")).toEqual(["say", "yell"]);
  });

  it("stores the pairing with a CryptoKey and wipes everything", async () => {
    const aesKey = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
    await setPairing({ deviceId: "dev", token: "d.dev.s", aesKey, pluginPublicKey: "p", devicePublicKey: "d", fingerprint: "123 456" });
    await setMeta("lastSeenPush", 7);
    await addMessages([item("a", 1)], 10);

    const pairing = await getPairing();
    expect(pairing?.deviceId).toBe("dev");
    expect(pairing?.aesKey).toBeInstanceOf(CryptoKey);

    await wipeAll();
    expect(await getPairing()).toBeUndefined();
    expect(await getMeta("lastSeenPush")).toBe(0);
    expect(await loadMessages(10)).toEqual([]);
  });
});
