import "fake-indexeddb/auto";
import { IDBFactory } from "fake-indexeddb";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, type Api } from "./api";
import { openPayload, sealPayload } from "./crypto";
import type { ChatItem, ClientFrame, DevicePayload, PluginPayload, ServerFrame } from "./protocol";
import type { RelayHandlers } from "./relay";
import { createSession, SEND_TIMEOUT_MS, type SessionDeps } from "./session";
import { DEFAULT_CACHE_LIMIT, openAccountStore, resetStorageForTests, type AccountStore } from "./storage";

let key: CryptoKey;
let store: AccountStore;

function item(id: string, ts: number, text = id): ChatItem {
  return { id, ts, channel: "say", sender: "Y'shtola Rhul", senderWorld: "Twintania", text, character: "Alpha Beta", outgoing: false };
}

function fakeApi(overrides: Partial<Api> = {}): Api {
  const fail = () => Promise.reject(new Error("not expected"));
  return {
    lookupPairing: fail,
    claimPairing: fail,
    getMe: () => Promise.resolve({ deviceId: "dev", status: "active" }),
    deleteDevice: () => Promise.resolve(),
    putPush: () => Promise.resolve(),
    deletePush: () => Promise.resolve(),
    getVapid: fail,
    ...overrides,
  };
}

function fakeRelay() {
  const sent: ClientFrame[] = [];
  let handlers: RelayHandlers | undefined;
  const relay = {
    sent,
    closed: false,
    connects: 0,
    connect(_token: string, h: RelayHandlers) {
      handlers = h;
      relay.connects++;
      return {
        send(frame: ClientFrame) {
          sent.push(frame);
          return true;
        },
        close() {
          relay.closed = true;
        },
      };
    },
    deliver(frame: ServerFrame) {
      handlers!.onFrame(frame);
    },
    drop() {
      handlers!.onClose();
    },
    async fromPlugin(payload: PluginPayload) {
      relay.deliver({ t: "msg", from: "plugin", payload: await sealPayload(key, "p2d", payload) });
    },
    async payloads(): Promise<DevicePayload[]> {
      const out: DevicePayload[] = [];
      for (const frame of sent) {
        if (frame.t === "send") out.push((await openPayload(key, "d2p", frame.payload)) as DevicePayload);
      }
      return out;
    },
  };
  return relay;
}

async function setup(opts: { approved?: boolean; api?: Partial<Api>; sendTimeoutMs?: number; cacheLimit?: number } = {}) {
  await store.setPairing({ deviceId: "dev", token: "d.dev.secret", aesKey: key, pluginPublicKey: "p", devicePublicKey: "d", fingerprint: "123 456" });
  await store.setMeta("approved", opts.approved ?? true);
  const relay = fakeRelay();
  const deps: SessionDeps = {
    api: fakeApi(opts.api),
    connect: relay.connect,
    push: { enable: () => Promise.resolve(true), disable: () => Promise.resolve() },
    store,
    cacheLimit: opts.cacheLimit ?? DEFAULT_CACHE_LIMIT,
    sendTimeoutMs: opts.sendTimeoutMs,
  };
  const session = await createSession(deps);
  return { session, relay };
}

beforeEach(async () => {
  resetStorageForTests();
  globalThis.indexedDB = new IDBFactory();
  store = openAccountStore("chatterror-test");
  key = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
});

describe("session", () => {
  it("is unpaired without a stored pairing and does not connect", async () => {
    const relay = fakeRelay();
    const session = await createSession({ api: fakeApi(), connect: relay.connect, push: { enable: async () => true, disable: async () => {} }, store, cacheLimit: DEFAULT_CACHE_LIMIT });
    expect(session.getState().status).toBe("unpaired");
    expect(relay.connects).toBe(0);
  });

  it("sends hello once the plugin is online and follows plugin status", async () => {
    const { session, relay } = await setup();
    expect(session.getState().status).toBe("connecting");
    expect(relay.sent).toEqual([]);

    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    await vi.waitFor(() => expect(session.getState().status).toBe("gameOffline"));
    expect(relay.sent).toEqual([]);

    relay.deliver({ t: "pluginStatus", online: true });
    await vi.waitFor(() => expect(session.getState().status).toBe("online"));
    await vi.waitFor(async () => expect((await relay.payloads())[0]).toMatchObject({ type: "hello", sinceTs: 0 }));

    relay.deliver({ t: "pluginStatus", online: true });
    await new Promise((r) => setTimeout(r, 20));
    expect((await relay.payloads()).filter((p) => p.type === "hello")).toHaveLength(1);

    relay.drop();
    await vi.waitFor(() => expect(session.getState().status).toBe("relayOffline"));
  });

  it("resends hello when the plugin comes back", async () => {
    const { relay } = await setup();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    relay.deliver({ t: "pluginStatus", online: false });
    relay.deliver({ t: "pluginStatus", online: true });
    await vi.waitFor(async () => expect((await relay.payloads()).filter((p) => p.type === "hello")).toHaveLength(2));
  });

  it("does not move the sync point past live chat until the backlog is done", async () => {
    const { relay } = await setup();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    const seq = Date.now();
    await relay.fromPlugin({ type: "backlog", seq, items: [item("a", 100)], done: false });
    await relay.fromPlugin({ type: "chat", seq: seq + 1, item: item("live", 900) });
    await vi.waitFor(async () => expect(await store.loadMessages(10)).toHaveLength(2));
    expect(await store.getMeta("syncTs")).toBe(100);

    await relay.fromPlugin({ type: "backlog", seq: seq + 2, items: [item("b", 200)], done: true });
    await vi.waitFor(async () => expect(await store.getMeta("syncTs")).toBe(900));
  });

  it("drops the deferred sync point when the socket closes mid backlog", async () => {
    const { relay } = await setup();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    const seq = Date.now();
    await relay.fromPlugin({ type: "backlog", seq, items: [item("a", 100)], done: false });
    await relay.fromPlugin({ type: "chat", seq: seq + 1, item: item("live", 900) });
    await vi.waitFor(async () => expect(await store.loadMessages(10)).toHaveLength(2));

    relay.sent.length = 0;
    relay.drop();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    await vi.waitFor(async () => expect((await relay.payloads())[0]).toMatchObject({ type: "hello", sinceTs: 100 }));
  });

  it("asks for messages newer than the last one received over the socket", async () => {
    const { relay } = await setup();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    await relay.fromPlugin({ type: "chat", seq: Date.now(), item: item("a", 500) });
    await vi.waitFor(async () => expect(await store.loadMessages(10)).toHaveLength(1));
    await store.addMessages([item("pushed", 900)], 100);

    relay.sent.length = 0;
    relay.drop();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    await vi.waitFor(async () => expect((await relay.payloads())[0]).toMatchObject({ type: "hello", sinceTs: 500 }));
  });

  it("pending device sends hello only once paired", async () => {
    const { session, relay } = await setup({ approved: false, api: { getMe: async () => ({ deviceId: "dev", status: "pending" }) } });
    expect(session.getState().status).toBe("pending");
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    await new Promise((r) => setTimeout(r, 20));
    expect(await relay.payloads()).toEqual([]);
    expect(session.getState().status).toBe("pending");

    relay.deliver({ t: "paired" });
    await vi.waitFor(() => expect(session.getState().status).toBe("online"));
    await vi.waitFor(async () => expect((await relay.payloads())[0]).toMatchObject({ type: "hello" }));
    expect(await store.getMeta("approved")).toBe(true);
  });

  it("pending device that was approved while away recovers through getMe", async () => {
    const { session, relay } = await setup({ approved: false });
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    await vi.waitFor(() => expect(session.getState().status).toBe("gameOffline"));
    relay.deliver({ t: "pluginStatus", online: true });
    await vi.waitFor(async () => expect((await relay.payloads())[0]).toMatchObject({ type: "hello" }));
  });

  it("stays pending unless getMe reports the device active", async () => {
    const { session, relay } = await setup({ approved: false, api: { getMe: async () => ({ deviceId: "dev", status: "expired" }) } });
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    await new Promise((r) => setTimeout(r, 20));
    expect(session.getState().status).toBe("pending");
    expect(await store.getMeta("approved")).toBe(false);
  });

  it("dedups chat by id and emits backlog oldest first", async () => {
    const { session, relay } = await setup();
    const seen: string[][] = [];
    session.onMessages((items) => seen.push(items.map((i) => i.id)));
    relay.deliver({ t: "authOk", role: "device", id: "dev" });

    const seq = Date.now();
    await relay.fromPlugin({ type: "chat", seq, item: item("a", 100) });
    await relay.fromPlugin({ type: "chat", seq: seq + 1, item: item("a", 100) });
    await relay.fromPlugin({ type: "backlog", seq: seq + 2, items: [item("c", 300), item("a", 100), item("b", 200)], done: true });

    await vi.waitFor(() => expect(seen).toEqual([["a"], ["b", "c"]]));
    expect((await session.loadHistory(10)).map((i) => i.id)).toEqual(["a", "b", "c"]);
  });

  it("emits messages the service worker stored while the page was open, but not loaded history", async () => {
    const { session, relay } = await setup();
    await store.addMessages([item("old", 50)], 100);
    expect((await session.loadHistory(10)).map((i) => i.id)).toEqual(["old"]);
    const seen: string[][] = [];
    session.onMessages((items) => seen.push(items.map((i) => i.id)));

    await store.addMessages([item("pushed", 100)], 100);
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    await relay.fromPlugin({ type: "backlog", seq: Date.now(), items: [item("old", 50), item("pushed", 100)], done: true });
    await vi.waitFor(() => expect(seen).toEqual([["pushed"]]));
  });

  it("drops replayed payloads", async () => {
    const { session, relay } = await setup();
    const seen: string[] = [];
    session.onMessages((items) => seen.push(...items.map((i) => i.id)));
    relay.deliver({ t: "authOk", role: "device", id: "dev" });

    const seq = Date.now();
    await relay.fromPlugin({ type: "chat", seq, item: item("a", 100) });
    await relay.fromPlugin({ type: "chat", seq, item: item("b", 200) });
    await relay.fromPlugin({ type: "chat", seq: seq - 5, item: item("c", 300) });
    await relay.fromPlugin({ type: "chat", seq: seq + 1, item: item("d", 400) });

    await vi.waitFor(() => expect(seen).toEqual(["a", "d"]));
    expect(await store.getMeta("lastSeenWs")).toBe(seq + 1);
  });

  it("drops payloads that fail to decrypt", async () => {
    const { session, relay } = await setup();
    const seen: string[] = [];
    session.onMessages((items) => seen.push(...items.map((i) => i.id)));
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "msg", from: "plugin", payload: "AQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" });
    await relay.fromPlugin({ type: "chat", seq: Date.now(), item: item("ok", 1) });
    await vi.waitFor(() => expect(seen).toEqual(["ok"]));
  });

  it("applies settings", async () => {
    const { session, relay } = await setup();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    await relay.fromPlugin({ type: "settings", seq: Date.now(), character: "Alpha Beta", relayChannels: ["say", "tell"], sendChannels: ["say"], maxLength: 300 });
    await vi.waitFor(() => expect(session.getState()).toMatchObject({ character: "Alpha Beta", relayChannels: ["say", "tell"], sendChannels: ["say"], maxLength: 300 }));
    expect((await store.getMeta("lastSettings"))?.maxLength).toBe(300);
  });

  it("send resolves on the matching sendResult", async () => {
    const { session, relay } = await setup();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    await vi.waitFor(() => expect(session.getState().status).toBe("online"));

    const result = session.send("tell", "hello", "Y'shtola Rhul@Twintania");
    let request: DevicePayload | undefined;
    await vi.waitFor(async () => {
      request = (await relay.payloads()).find((p) => p.type === "sendChat");
      expect(request).toBeDefined();
    });
    expect(request).toMatchObject({ type: "sendChat", channel: "tell", target: "Y'shtola Rhul@Twintania", text: "hello" });
    const requestId = request!.type === "sendChat" ? request!.requestId : "";

    await relay.fromPlugin({ type: "sendResult", seq: Date.now() + 1000, requestId: "other", ok: false, error: "busy" });
    await relay.fromPlugin({ type: "sendResult", seq: Date.now() + 2000, requestId, ok: false, error: "notLoggedIn" });
    expect(await result).toEqual({ ok: false, error: "notLoggedIn" });
  });

  it("send times out without a sendResult", async () => {
    const { session, relay } = await setup({ sendTimeoutMs: 30 });
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    await vi.waitFor(() => expect(session.getState().status).toBe("online"));
    expect(await session.send("say", "anyone?")).toEqual({ ok: false, error: "timeout" });
  });

  it("send fails fast when not connected", async () => {
    const { session } = await setup();
    expect(await session.send("say", "hi")).toEqual({ ok: false, error: "offline" });
  });

  it("send fails fast with gameOffline when the plugin is known offline", async () => {
    const { session, relay } = await setup();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: false });
    await vi.waitFor(() => expect(session.getState().status).toBe("gameOffline"));
    await new Promise((r) => setTimeout(r, 10));

    expect(await session.send("say", "hi")).toEqual({ ok: false, error: "gameOffline" });
    expect((await relay.payloads()).filter((p) => p.type === "sendChat")).toEqual([]);
  });

  it("waits long enough for a full plugin send queue", () => {
    expect(SEND_TIMEOUT_MS).toBe(45_000);
  });

  it("sends prefs when muting", async () => {
    const { session, relay } = await setup();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    await session.setMuted(["shout"]);
    expect(session.getState().mutedChannels).toEqual(["shout"]);
    expect(await store.getMeta("mutedChannels")).toEqual(["shout"]);
    await vi.waitFor(async () => expect((await relay.payloads()).some((p) => p.type === "prefs" && p.mutedChannels[0] === "shout")).toBe(true));
  });

  it("sends channel prefs with hello and when they change", async () => {
    await store.setMeta("channelPrefs", { pinned: [], muted: ["c|Alpha Beta|say"], notify: {} });
    const { session, relay } = await setup();
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    relay.deliver({ t: "pluginStatus", online: true });
    await vi.waitFor(async () =>
      expect((await relay.payloads()).find((p) => p.type === "prefs")).toMatchObject({
        mutedChannels: [],
        channels: [{ character: "Alpha Beta", channel: "say", notify: "none" }],
      }),
    );

    const prefs = { pinned: ["c|Alpha Beta|say"], muted: [], notify: { "t|Alpha Beta|Foo Bar@World": "all" as const } };
    await session.setChannelPrefs(prefs);
    expect(session.getState().channelPrefs).toEqual(prefs);
    expect(await store.getMeta("channelPrefs")).toEqual(prefs);
    const sent = (await relay.payloads()).filter((p) => p.type === "prefs");
    expect(sent[sent.length - 1]).toMatchObject({
      channels: [{ character: "Alpha Beta", channel: "tell", partner: "Foo Bar@World", notify: "all" }],
    });
  });

  it("revoked frame wipes storage", async () => {
    const { session, relay } = await setup();
    await store.addMessages([item("a", 1)], 100);
    relay.deliver({ t: "revoked" });
    await vi.waitFor(() => expect(session.getState().status).toBe("revoked"));
    expect(relay.closed).toBe(true);
    expect(await store.getPairing()).toBeUndefined();
    expect(await store.loadMessages(10)).toEqual([]);
  });

  it("401 from the api revokes", async () => {
    const { session, relay } = await setup({ api: { getMe: () => Promise.reject(new ApiError(401, "unauthorized")) } });
    relay.deliver({ t: "authFail" });
    await vi.waitFor(() => expect(session.getState().status).toBe("revoked"));
    expect(await store.getPairing()).toBeUndefined();
  });

  it("unpair deletes the device and wipes storage", async () => {
    const deleted: string[] = [];
    const { session, relay } = await setup({ api: { deleteDevice: async (_token, id) => void deleted.push(id) } });
    await session.unpair();
    expect(deleted).toEqual(["dev"]);
    expect(relay.closed).toBe(true);
    expect(session.getState().status).toBe("unpaired");
    expect(await store.getPairing()).toBeUndefined();
  });

  it("setCacheLimit trims the store and clearCache empties it", async () => {
    const { session } = await setup();
    await store.addMessages([item("a", 1), item("b", 2), item("c", 3)], 100);
    await session.setCacheLimit(2);
    expect((await session.loadHistory(10)).map((i) => i.id)).toEqual(["b", "c"]);
    await session.clearCache();
    expect(await session.loadHistory(10)).toEqual([]);
  });

  it("exposes the cache limit in state and keeps it across unpair", async () => {
    const { session } = await setup();
    expect(session.getState().cacheLimit).toBe(DEFAULT_CACHE_LIMIT);

    await session.setCacheLimit(500);
    expect(session.getState().cacheLimit).toBe(500);

    await session.unpair();
    expect(session.getState().cacheLimit).toBe(500);
  });

  it("starts with the cache limit it is given", async () => {
    const { session } = await setup({ cacheLimit: 5000 });
    expect(session.getState().cacheLimit).toBe(5000);
  });

  it("notifies subscribers of state changes", async () => {
    const { session, relay } = await setup();
    const statuses: string[] = [];
    const off = session.subscribe((s) => statuses.push(s.status));
    relay.deliver({ t: "authOk", role: "device", id: "dev" });
    await vi.waitFor(() => expect(statuses).toContain("gameOffline"));
    off();
    relay.drop();
    await new Promise((r) => setTimeout(r, 10));
    expect(statuses).not.toContain("relayOffline");
  });
});
