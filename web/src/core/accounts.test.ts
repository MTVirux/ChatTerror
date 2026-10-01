import "fake-indexeddb/auto";
import { IDBFactory } from "fake-indexeddb";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Api } from "./api";
import { encode } from "./b64url";
import { exportPublicRaw, generateDeviceKey, sealPayload } from "./crypto";
import type { ChatItem, PluginPayload, ServerFrame } from "./protocol";
import type { RelayHandlers } from "./relay";
import { createAccountManager, type ManagerDeps } from "./accounts";
import { addAccount, listAccounts, resetRegistryForTests } from "./registry";
import { accountDbName, openAccountStore, resetStorageForTests } from "./storage";

const keys = new Map<string, CryptoKey>();

async function seed(deviceId: string, opts: { push?: boolean } = {}) {
  const aesKey = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
  keys.set(deviceId, aesKey);
  const store = openAccountStore(accountDbName(deviceId));
  await store.setPairing({ deviceId, token: `tok-${deviceId}`, aesKey, pluginPublicKey: `pk-${deviceId}`, devicePublicKey: "d", fingerprint: "1 2" });
  await store.setMeta("approved", true);
  await store.setMeta("pushEnabled", opts.push ?? false);
  await addAccount({ deviceId, dbName: accountDbName(deviceId), pluginPublicKey: `pk-${deviceId}`, label: "", addedAt: 1, status: "active" });
}

function fakeDeps(api: Partial<Api> = {}) {
  const handlers = new Map<string, RelayHandlers>();
  const push = { enable: vi.fn(async () => true), disable: vi.fn(async () => {}) };
  const fail = () => Promise.reject(new Error("not expected"));
  const deps: ManagerDeps = {
    api: {
      lookupPairing: fail,
      claimPairing: fail,
      getMe: async () => ({ deviceId: "x", status: "active" }),
      deleteDevice: vi.fn(async () => {}),
      putPush: async () => {},
      deletePush: vi.fn(async () => {}),
      getVapid: fail,
      ...api,
    },
    connect: (token, h) => {
      handlers.set(token, h);
      return { send: () => true, close: () => {} };
    },
    push,
  };
  const deliver = (deviceId: string, frame: ServerFrame) => handlers.get(`tok-${deviceId}`)!.onFrame(frame);
  const fromPlugin = async (deviceId: string, payload: PluginPayload) =>
    deliver(deviceId, { t: "msg", from: "plugin", payload: await sealPayload(keys.get(deviceId)!, "p2d", payload) });
  const chat = (deviceId: string, item: ChatItem, seq: number) => fromPlugin(deviceId, { type: "chat", seq, item });
  const online = (deviceId: string) => {
    deliver(deviceId, { t: "authOk", role: "device", id: deviceId });
    deliver(deviceId, { t: "pluginStatus", online: true });
  };
  return { deps, push, deliver, fromPlugin, chat, online };
}

function item(id: string, ts: number, character = "Alpha Beta"): ChatItem {
  return { id, ts, channel: "say", sender: "Y'shtola Rhul", senderWorld: "Twintania", text: id, character, outgoing: false };
}

async function pluginKey(): Promise<string> {
  const plugin = await generateDeviceKey();
  return encode(await exportPublicRaw(plugin.publicKey));
}

beforeEach(() => {
  resetStorageForTests();
  resetRegistryForTests();
  globalThis.indexedDB = new IDBFactory();
  keys.clear();
});

describe("account manager", () => {
  it("opens a session per account in order", async () => {
    await seed("a");
    await seed("b");
    const { deps } = fakeDeps();
    const manager = await createAccountManager(deps);
    expect(manager.list().map((a) => a.deviceId)).toEqual(["a", "b"]);
    expect(manager.list().map((a) => a.label)).toEqual(["Account 1", "Account 2"]);
    expect(manager.session("a")).toBeDefined();
    expect(manager.session("b")).toBeDefined();
  });

  it("pairs a second account without touching the first", async () => {
    await seed("a");
    const pluginPublicKey = await pluginKey();
    const { deps } = fakeDeps({
      lookupPairing: async () => ({ installId: "inst", pluginPublicKey }),
      claimPairing: async () => ({ deviceId: "b", deviceToken: "tok-b" }),
    });
    const manager = await createAccountManager(deps);
    const result = await manager.pair("ABCD-EFGH-2345-6789", "Phone");

    expect(result.deviceId).toBe("b");
    expect(manager.list().map((a) => a.deviceId)).toEqual(["a", "b"]);
    expect(await openAccountStore(accountDbName("a")).getPairing()).toBeDefined();
    expect(await openAccountStore(accountDbName("b")).getPairing()).toMatchObject({ deviceId: "b", pluginPublicKey, fingerprint: result.fingerprint });
    expect(await listAccounts()).toHaveLength(2);
  });

  it("refuses pairing the same install twice", async () => {
    await seed("a");
    const pluginPublicKey = await pluginKey();
    await addAccount({ deviceId: "a", dbName: accountDbName("a"), pluginPublicKey, label: "", addedAt: 1, status: "active" });
    const { deps } = fakeDeps({
      lookupPairing: async () => ({ installId: "inst", pluginPublicKey }),
      claimPairing: async () => ({ deviceId: "b", deviceToken: "tok-b" }),
    });
    const manager = await createAccountManager(deps);
    await expect(manager.pair("ABCD-EFGH-2345-6789", "Phone")).rejects.toMatchObject({ code: "alreadyPaired" });
    expect(manager.list().map((a) => a.deviceId)).toEqual(["a"]);
  });

  it("removing one account keeps the other", async () => {
    await seed("a");
    await seed("b");
    const { deps } = fakeDeps();
    const manager = await createAccountManager(deps);
    await manager.remove("a");

    expect(manager.list().map((a) => a.deviceId)).toEqual(["b"]);
    expect((await listAccounts()).map((a) => a.deviceId)).toEqual(["b"]);
    expect(await openAccountStore(accountDbName("a")).getPairing()).toBeUndefined();
    expect(await openAccountStore(accountDbName("b")).getPairing()).toBeDefined();
    expect(deps.api.deleteDevice).toHaveBeenCalledWith("tok-a", "a");
  });

  it("keeps the browser push subscription while another account uses it", async () => {
    await seed("a", { push: true });
    await seed("b", { push: true });
    const { deps, push } = fakeDeps();
    const manager = await createAccountManager(deps);

    await manager.session("a")!.disablePush();
    expect(push.disable).not.toHaveBeenCalled();
    expect(deps.api.deletePush).toHaveBeenCalledWith("tok-a");

    await manager.session("b")!.disablePush();
    expect(push.disable).toHaveBeenCalledTimes(1);
    expect(push.disable).toHaveBeenCalledWith("tok-b");
  });

  it("a revoke only affects that account", async () => {
    await seed("a");
    await seed("b");
    const { deps, deliver } = fakeDeps();
    const manager = await createAccountManager(deps);
    deliver("a", { t: "revoked" });

    await vi.waitFor(() => expect(manager.list().find((x) => x.deviceId === "a")?.status).toBe("revoked"));
    await vi.waitFor(async () => expect((await listAccounts()).find((x) => x.deviceId === "a")!.status).toBe("revoked"));
    expect(manager.list().find((x) => x.deviceId === "b")?.status).toBe("active");
    expect(await openAccountStore(accountDbName("b")).getPairing()).toBeDefined();
  });

  it("takes the label from the plugin settings", async () => {
    await seed("a");
    const { deps, online, fromPlugin } = fakeDeps();
    const manager = await createAccountManager(deps);
    online("a");
    await fromPlugin("a", { type: "settings", seq: Date.now(), character: "Alpha Beta", relayChannels: ["say"], sendChannels: ["say"], maxLength: 500 });

    await vi.waitFor(() => expect(manager.list()[0].label).toBe("Alpha Beta"));
    await vi.waitFor(async () => expect((await listAccounts())[0].label).toBe("Alpha Beta"));
  });

  it("counts unread for accounts not being viewed", async () => {
    await seed("a");
    await seed("b");
    const { deps, online, chat } = fakeDeps();
    const manager = await createAccountManager(deps);
    manager.setViewing("a");
    online("a");
    online("b");
    const unread = (id: string) => manager.list().find((x) => x.deviceId === id)!.unread;

    await chat("b", item("x", 1), Date.now());
    await vi.waitFor(() => expect(unread("b")).toBe(1));
    expect(unread("a")).toBe(0);

    manager.setViewing("b");
    expect(unread("b")).toBe(0);

    manager.setViewing("all");
    const seen: string[] = [];
    manager.onMessages((items) => seen.push(...items.map((i) => `${i.deviceId}:${i.id}`)));
    await chat("a", item("y", 2), Date.now() + 1);
    await vi.waitFor(() => expect(seen).toEqual(["a:y"]));
    expect(unread("a")).toBe(0);
    expect(unread("b")).toBe(0);
  });

  it("merges history from every account by time", async () => {
    await seed("a");
    await seed("b");
    await openAccountStore(accountDbName("a")).addMessages([item("1", 1), item("3", 3)], 100);
    await openAccountStore(accountDbName("b")).addMessages([item("2", 2)], 100);
    const { deps } = fakeDeps();
    const manager = await createAccountManager(deps);

    const all = await manager.loadMerged(10);
    expect(all.map((i) => i.id)).toEqual(["1", "2", "3"]);
    expect(all.map((i) => i.deviceId)).toEqual(["a", "b", "a"]);
    expect((await manager.loadMerged(2)).map((i) => i.id)).toEqual(["2", "3"]);
  });

  it("drops an active account whose pairing is gone", async () => {
    await addAccount({ deviceId: "z", dbName: accountDbName("z"), pluginPublicKey: "pk-z", label: "", addedAt: 1, status: "active" });
    const { deps } = fakeDeps();
    const manager = await createAccountManager(deps);
    expect(manager.list()).toEqual([]);
    expect(await listAccounts()).toEqual([]);
  });

  it("starts the other accounts when one account's database can't open", async () => {
    await seed("a");
    await seed("b");
    // A newer schema version than the app knows makes opening that database fail.
    await new Promise<void>((resolve) => {
      const request = indexedDB.open("chatterror-broken", 99);
      request.onsuccess = () => {
        request.result.close();
        resolve();
      };
    });
    await addAccount({ deviceId: "broken", dbName: "chatterror-broken", pluginPublicKey: "pk-broken", label: "", addedAt: 1, status: "active" });

    const manager = await createAccountManager(fakeDeps().deps);
    expect(manager.list().map((a) => a.deviceId)).toEqual(["a", "b"]);
    expect((await listAccounts()).map((r) => r.deviceId)).toContain("broken");
  });

  it("persists the cache limit and applies it to every session", async () => {
    await seed("a");
    await seed("b");
    const { deps } = fakeDeps();
    const manager = await createAccountManager(deps);
    await manager.setCacheLimit(500);

    expect(manager.cacheLimit()).toBe(500);
    expect(manager.list().map((a) => a.state.cacheLimit)).toEqual([500, 500]);
    manager.close();
    resetRegistryForTests();
    expect((await createAccountManager(fakeDeps().deps)).cacheLimit()).toBe(500);
  });
});
