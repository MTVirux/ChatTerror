import "fake-indexeddb/auto";
import { IDBFactory } from "fake-indexeddb";
import { beforeEach, describe, expect, it } from "vitest";
import { addAccount, getCacheLimit, listAccounts, removeAccount, resetRegistryForTests, setCacheLimit, updateAccount } from "./registry";
import { LEGACY_DB_NAME, openAccountStore, resetStorageForTests } from "./storage";

async function seedLegacy(deviceId = "old") {
  const legacy = openAccountStore(LEGACY_DB_NAME);
  const aesKey = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
  await legacy.setPairing({ deviceId, token: "t", aesKey, pluginPublicKey: "pk-old", devicePublicKey: "d", fingerprint: "1 2" });
  await legacy.setMeta("cacheLimit", 500);
}

function record(deviceId: string) {
  return { deviceId, dbName: `chatterror-${deviceId}`, pluginPublicKey: `pk-${deviceId}`, label: "", addedAt: 1, status: "active" as const };
}

beforeEach(() => {
  resetStorageForTests();
  resetRegistryForTests();
  globalThis.indexedDB = new IDBFactory();
});

describe("registry", () => {
  it("adopts the legacy pairing and its cache limit", async () => {
    await seedLegacy();
    const accounts = await listAccounts();
    expect(accounts).toMatchObject([{ deviceId: "old", dbName: LEGACY_DB_NAME, pluginPublicKey: "pk-old", status: "active", order: 0 }]);
    expect(await getCacheLimit()).toBe(500);
  });

  it("adopts nothing without a legacy pairing", async () => {
    expect(await listAccounts()).toEqual([]);
    expect(await getCacheLimit()).toBe(2000);
  });

  it("adopts only once, even after the account is removed", async () => {
    await seedLegacy();
    await listAccounts();
    await removeAccount("old");
    resetRegistryForTests();
    expect(await listAccounts()).toEqual([]);
  });

  it("adds accounts in order, updates and removes them", async () => {
    await addAccount(record("a"));
    await addAccount(record("b"));
    await updateAccount("a", { label: "Alpha Beta", status: "revoked" });
    expect((await listAccounts()).map((a) => [a.deviceId, a.order, a.label, a.status])).toEqual([
      ["a", 0, "Alpha Beta", "revoked"],
      ["b", 1, "", "active"],
    ]);
    await removeAccount("a");
    expect((await listAccounts()).map((a) => a.deviceId)).toEqual(["b"]);
  });

  it("stores a custom name", async () => {
    await addAccount(record("a"));
    await updateAccount("a", { name: "Main" });
    expect(await listAccounts()).toMatchObject([{ deviceId: "a", name: "Main" }]);
    await updateAccount("a", { name: undefined });
    expect((await listAccounts())[0].name).toBeUndefined();
  });

  it("stores the cache limit", async () => {
    await setCacheLimit(5000);
    expect(await getCacheLimit()).toBe(5000);
  });
});
