import { ApiError, createApi, type Api } from "./api";
import { decode, encode } from "./b64url";
import { deriveKey, exportPublicRaw, fingerprint, generateDeviceKey, openPayload, sealPayload } from "./crypto";
import { parsePluginPayload, type ChatChannel, type ChatItem, type DevicePayload, type PluginPayload, type ServerFrame } from "./protocol";
import { createPushControl, registerServiceWorker, type PushControl } from "./push";
import { connectRelay, type RelayConnection, type RelayHandlers } from "./relay";
import { SeqCounter, SeqGuard } from "./seq";
import {
  addMessages,
  clearMessages,
  DEFAULT_CACHE_LIMIT,
  getMeta,
  getPairing,
  loadMessages,
  setMeta,
  setPairing,
  trimMessages,
  wipeAll,
  type Pairing,
} from "./storage";

export type SessionStatus = "unpaired" | "pending" | "connecting" | "online" | "gameOffline" | "relayOffline" | "revoked";

export interface SessionState {
  status: SessionStatus;
  fingerprint?: string;
  character?: string;
  relayChannels: ChatChannel[];
  sendChannels: ChatChannel[];
  maxLength: number;
  mutedChannels: ChatChannel[];
  pushEnabled: boolean;
}

export interface SendResult {
  ok: boolean;
  error?: string;
}

export interface Session {
  getState(): SessionState;
  subscribe(cb: (s: SessionState) => void): () => void;
  onMessages(cb: (items: ChatItem[]) => void): () => void;
  loadHistory(limit: number): Promise<ChatItem[]>;
  send(channel: ChatChannel, text: string, target?: string): Promise<SendResult>;
  setMuted(channels: ChatChannel[]): Promise<void>;
  enablePush(): Promise<boolean>;
  disablePush(): Promise<void>;
  clearCache(): Promise<void>;
  setCacheLimit(n: number): Promise<void>;
  unpair(): Promise<void>;
  close(): void;
}

export interface SessionDeps {
  api: Api;
  connect(token: string, handlers: RelayHandlers): RelayConnection;
  push: PushControl;
  sendTimeoutMs?: number;
}

interface InternalSession extends Session {
  restart(): Promise<void>;
}

type WithoutSeq<P> = P extends unknown ? Omit<P, "seq"> : never;
type OutgoingPayload = WithoutSeq<DevicePayload>;

const DEFAULT_MAX_LENGTH = 500;
const SEND_TIMEOUT_MS = 15000;
const CROCKFORD = /^[0-9A-HJKMNP-TV-Z]{8}$/;

const liveSessions = new Set<InternalSession>();

function emptyState(status: SessionStatus): SessionState {
  return { status, relayChannels: [], sendChannels: [], maxLength: DEFAULT_MAX_LENGTH, mutedChannels: [], pushEnabled: false };
}

export function parsePairCode(input: string): string | null {
  const fromUrl = input.match(/[#?&]pair=([^&\s]+)/i);
  let raw = fromUrl ? decodeURIComponent(fromUrl[1]) : input;
  raw = raw.replace(/[\s-]/g, "").toUpperCase().replace(/O/g, "0").replace(/[IL]/g, "1");
  return CROCKFORD.test(raw) ? `${raw.slice(0, 4)}-${raw.slice(4)}` : null;
}

// Lookup, keygen, claim, derive and store. Throws ApiError with a code such as invalidCode, notFound or tooManyDevices.
export async function pairDevice(api: Api, input: string, deviceName: string): Promise<{ fingerprint: string }> {
  const code = parsePairCode(input);
  if (!code) throw new ApiError(400, "invalidCode");

  const { pluginPublicKey } = await api.lookupPairing(code);
  const keys = await generateDeviceKey();
  const pluginPub = decode(pluginPublicKey);
  const devicePub = await exportPublicRaw(keys.publicKey);
  const aesKey = await deriveKey(keys.privateKey, pluginPub, devicePub);
  const print = await fingerprint(pluginPub, devicePub);
  const devicePublicKey = encode(devicePub);
  const { deviceId, deviceToken } = await api.claimPairing(code, devicePublicKey, deviceName);

  await wipeAll();
  await setPairing({ deviceId, token: deviceToken, aesKey, pluginPublicKey, devicePublicKey, fingerprint: print });
  return { fingerprint: print };
}

export async function createSession(deps: SessionDeps): Promise<InternalSession> {
  const sendTimeoutMs = deps.sendTimeoutMs ?? SEND_TIMEOUT_MS;
  const stateListeners = new Set<(s: SessionState) => void>();
  const messageListeners = new Set<(items: ChatItem[]) => void>();
  const pendingSends = new Map<string, (result: SendResult) => void>();
  // Ids already handed to the UI. The service worker may have stored a message the open page never showed.
  const shownIds = new Set<string>();

  let state = emptyState("unpaired");
  let pairing: Pairing | undefined;
  let connection: RelayConnection | null = null;
  let generation = 0;
  let queue = Promise.resolve();

  let approved = false;
  let authed = false;
  let dropped = false;
  // undefined until the relay or a plugin message tells us.
  let pluginOnline: boolean | undefined;
  let guard = new SeqGuard();
  let counter = new SeqCounter();
  let syncTs = 0;
  // Live chat can arrive before the backlog finishes; its ts must not move the sync point past missing backlog items.
  let backlogPending = false;
  let deferredTs = 0;
  let cacheLimit = DEFAULT_CACHE_LIMIT;

  function setState(patch: Partial<SessionState>) {
    state = { ...state, ...patch };
    for (const listener of stateListeners) listener(state);
  }

  function connectionStatus(): SessionStatus {
    if (!approved) return "pending";
    if (!authed) return dropped ? "relayOffline" : "connecting";
    return pluginOnline ? "online" : "gameOffline";
  }

  function refreshStatus() {
    if (pairing && state.status !== connectionStatus()) setState({ status: connectionStatus() });
  }

  function stopConnection() {
    generation++;
    connection?.close();
    connection = null;
    authed = false;
    for (const finish of [...pendingSends.values()]) finish({ ok: false, error: "offline" });
  }

  async function start() {
    stopConnection();
    pairing = await getPairing();
    shownIds.clear();
    const [settings, muted, push, limit, lastSeenWs, lastSeqSent, storedSyncTs, storedApproved] = await Promise.all([
      getMeta("lastSettings"),
      getMeta("mutedChannels"),
      getMeta("pushEnabled"),
      getMeta("cacheLimit"),
      getMeta("lastSeenWs"),
      getMeta("lastSeqSent"),
      getMeta("syncTs"),
      getMeta("approved"),
    ]);
    guard = new SeqGuard(lastSeenWs);
    counter = new SeqCounter(lastSeqSent);
    syncTs = storedSyncTs;
    cacheLimit = limit;
    approved = storedApproved;
    dropped = false;
    pluginOnline = undefined;

    if (!pairing) {
      setState(emptyState(state.status === "revoked" ? "revoked" : "unpaired"));
      return;
    }

    state = {
      ...emptyState(approved ? "connecting" : "pending"),
      fingerprint: pairing.fingerprint,
      character: settings?.character,
      relayChannels: settings?.relayChannels ?? [],
      sendChannels: settings?.sendChannels ?? [],
      maxLength: settings?.maxLength ?? DEFAULT_MAX_LENGTH,
      mutedChannels: muted,
      pushEnabled: push,
    };
    setState({});

    const current = ++generation;
    const enqueue = (work: () => Promise<void> | void) => {
      queue = queue.then(() => (current === generation ? work() : undefined)).catch(() => undefined);
    };
    connection = deps.connect(pairing.token, {
      onFrame: (frame) => enqueue(() => handleFrame(frame)),
      onClose: () => enqueue(handleClose),
    });
  }

  async function revoke() {
    stopConnection();
    pairing = undefined;
    await wipeAll();
    await deps.push.disable().catch(() => undefined);
    setState(emptyState("revoked"));
  }

  async function guarded<T>(promise: Promise<T>): Promise<T> {
    try {
      return await promise;
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) await revoke();
      throw error;
    }
  }

  async function sendPayload(payload: OutgoingPayload): Promise<boolean> {
    if (!pairing || !connection || !authed) return false;
    const seq = counter.next();
    void setMeta("lastSeqSent", seq);
    const envelope = await sealPayload(pairing.aesKey, "d2p", { ...payload, seq });
    return connection?.send({ t: "send", payload: envelope }) ?? false;
  }

  async function sendHello() {
    backlogPending = true;
    deferredTs = 0;
    await sendPayload({ type: "hello", sinceTs: syncTs });
    await sendPayload({ type: "prefs", mutedChannels: state.mutedChannels });
  }

  async function markApproved() {
    if (approved) return;
    approved = true;
    await setMeta("approved", true);
    refreshStatus();
    if (pluginOnline) await sendHello();
  }

  async function handleFrame(frame: ServerFrame) {
    switch (frame.t) {
      case "authOk":
        authed = true;
        dropped = false;
        refreshStatus();
        if (!approved && pairing) {
          const me = await guarded(deps.api.getMe(pairing.token));
          if (me.status === "active") await markApproved();
        }
        return;
      case "authFail":
        if (pairing) await guarded(deps.api.getMe(pairing.token));
        return;
      case "pluginStatus": {
        // The relay sends this right after authOk, so it is also what triggers the first hello.
        const cameBack = pluginOnline !== true && frame.online;
        pluginOnline = frame.online;
        refreshStatus();
        if (cameBack && authed && approved) await sendHello();
        return;
      }
      case "paired":
        await markApproved();
        return;
      case "revoked":
        await revoke();
        return;
      case "msg":
        await handleMsg(frame.payload);
        return;
    }
  }

  function handleClose() {
    authed = false;
    dropped = true;
    pluginOnline = undefined;
    backlogPending = false;
    refreshStatus();
  }

  async function handleMsg(envelope: string) {
    if (!pairing) return;
    let payload: PluginPayload | null;
    try {
      payload = parsePluginPayload(await openPayload(pairing.aesKey, "p2d", envelope));
    } catch {
      return;
    }
    if (!payload || !guard.accept(payload.seq)) return;
    await setMeta("lastSeenWs", payload.seq);
    if (!pluginOnline) {
      pluginOnline = true;
      refreshStatus();
    }

    switch (payload.type) {
      case "chat":
        await receiveItems([payload.item], !backlogPending);
        return;
      case "backlog":
        await receiveItems(payload.items, true);
        if (payload.done && backlogPending) {
          backlogPending = false;
          await advanceSync(deferredTs);
        }
        return;
      case "sendResult":
        pendingSends.get(payload.requestId)?.({ ok: payload.ok, ...(payload.error ? { error: payload.error } : {}) });
        return;
      case "settings":
        await setMeta("lastSettings", payload);
        setState({
          character: payload.character,
          relayChannels: payload.relayChannels,
          sendChannels: payload.sendChannels,
          maxLength: payload.maxLength,
        });
        return;
    }
  }

  async function advanceSync(ts: number) {
    if (ts <= syncTs) return;
    syncTs = ts;
    await setMeta("syncTs", ts);
  }

  async function receiveItems(items: ChatItem[], moveSync: boolean) {
    if (items.length === 0) return;
    const sorted = [...items].sort((a, b) => a.ts - b.ts);
    await addMessages(sorted, cacheLimit);
    const newest = sorted[sorted.length - 1].ts;
    if (moveSync) await advanceSync(newest);
    else deferredTs = Math.max(deferredTs, newest);
    const fresh: ChatItem[] = [];
    for (const item of sorted) {
      if (shownIds.has(item.id)) continue;
      shownIds.add(item.id);
      fresh.push(item);
    }
    if (fresh.length > 0) for (const listener of messageListeners) listener(fresh);
  }

  const session: InternalSession = {
    getState: () => state,

    subscribe(cb) {
      stateListeners.add(cb);
      return () => stateListeners.delete(cb);
    },

    onMessages(cb) {
      messageListeners.add(cb);
      return () => messageListeners.delete(cb);
    },

    async loadHistory(limit) {
      const items = await loadMessages(limit);
      for (const item of items) shownIds.add(item.id);
      return items;
    },

    send(channel, text, target) {
      if (!authed || !approved) return Promise.resolve({ ok: false, error: "offline" });
      const requestId = crypto.randomUUID();
      return new Promise((resolve) => {
        const finish = (result: SendResult) => {
          clearTimeout(timer);
          pendingSends.delete(requestId);
          resolve(result);
        };
        const timer = setTimeout(() => finish({ ok: false, error: "timeout" }), sendTimeoutMs);
        pendingSends.set(requestId, finish);
        sendPayload({ type: "sendChat", requestId, channel, target, text }).then(
          (sent) => sent || finish({ ok: false, error: "offline" }),
          () => finish({ ok: false, error: "offline" }),
        );
      });
    },

    async setMuted(channels) {
      setState({ mutedChannels: [...channels] });
      await setMeta("mutedChannels", [...channels]);
      await sendPayload({ type: "prefs", mutedChannels: [...channels] });
    },

    async enablePush() {
      if (!pairing) return false;
      const enabled = await guarded(deps.push.enable(pairing.token)).catch(() => false);
      if (!pairing) return false;
      await setMeta("pushEnabled", enabled);
      setState({ pushEnabled: enabled });
      return enabled;
    },

    async disablePush() {
      if (!pairing) return;
      await guarded(deps.push.disable(pairing.token)).catch(() => undefined);
      if (!pairing) return;
      await setMeta("pushEnabled", false);
      setState({ pushEnabled: false });
    },

    clearCache: () => clearMessages(),

    async setCacheLimit(n) {
      cacheLimit = n;
      await setMeta("cacheLimit", n);
      await trimMessages(n);
    },

    async unpair() {
      if (pairing) await deps.api.deleteDevice(pairing.token, pairing.deviceId).catch(() => undefined);
      await deps.push.disable().catch(() => undefined);
      stopConnection();
      pairing = undefined;
      await wipeAll();
      setState(emptyState("unpaired"));
    },

    close() {
      stopConnection();
      stateListeners.clear();
      messageListeners.clear();
      liveSessions.delete(session);
    },

    restart: start,
  };

  liveSessions.add(session);
  await start();
  return session;
}

export async function openSession(): Promise<Session> {
  void registerServiceWorker();
  const api = createApi();
  return createSession({ api, connect: connectRelay, push: createPushControl(api) });
}

export async function pair(code: string, deviceName: string): Promise<{ fingerprint: string }> {
  const result = await pairDevice(createApi(), code, deviceName);
  await Promise.all([...liveSessions].map((s) => s.restart()));
  return result;
}
