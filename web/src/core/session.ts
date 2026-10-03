import { ApiError, type Api } from "./api";
import { decode, encode } from "./b64url";
import { channelNotifyPrefs, EMPTY_CHANNEL_PREFS, withDefaultChannels, withDefaults, type ChannelPrefs } from "./channelPrefs";
import { deriveKey, exportPublicRaw, fingerprint, generateDeviceKey, generateTellKey, openPayload, sealPayload, verifyBundle } from "./crypto";
import { isValidTs, parsePluginPayload, parseSettings, type ChatChannel, type ChatItem, type DevicePayload, type PluginPayload, type ServerFrame, type TellBody, type TellBundle, type TellContact } from "./protocol";
import type { PushControl } from "./push";
import type { RelayConnection, RelayHandlers } from "./relay";
import { SeqCounter, SeqGuard } from "./seq";
import { DEFAULT_CACHE_LIMIT, type AccountStore, type Pairing } from "./storage";
import { buildCopies, findContact, openTellFrame, tellToItem } from "./tells";

export type SessionStatus = "unpaired" | "pending" | "connecting" | "online" | "gameOffline" | "relayOffline" | "revoked";

export interface SessionState {
  status: SessionStatus;
  fingerprint?: string;
  character?: string;
  characterWorld?: string;
  relayChannels: ChatChannel[];
  sendChannels: ChatChannel[];
  maxLength: number;
  contacts: TellContact[];
  mutedChannels: ChatChannel[];
  channelPrefs: ChannelPrefs;
  pushEnabled: boolean;
  cacheLimit: number;
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
  // character picks which of our characters a relayed tell goes out from.
  send(channel: ChatChannel, text: string, target?: string, character?: string): Promise<SendResult>;
  setMuted(channels: ChatChannel[]): Promise<void>;
  setChannelPrefs(prefs: ChannelPrefs): Promise<void>;
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
  store: AccountStore;
  cacheLimit: number;
  sendTimeoutMs?: number;
}

type WithoutSeq<P> = P extends unknown ? Omit<P, "seq"> : never;
type OutgoingPayload = WithoutSeq<DevicePayload>;

const DEFAULT_MAX_LENGTH = 500;
const CROCKFORD = /^[0-9A-HJKMNP-TV-Z]{16}$/;
// Covers a full plugin send queue.
export const SEND_TIMEOUT_MS = 45_000;

function emptyState(status: SessionStatus, cacheLimit = DEFAULT_CACHE_LIMIT): SessionState {
  return { status, relayChannels: [], sendChannels: [], maxLength: DEFAULT_MAX_LENGTH, contacts: [], mutedChannels: [], channelPrefs: EMPTY_CHANNEL_PREFS, pushEnabled: false, cacheLimit };
}

export interface PairCode {
  code: string;
  secret: string;
}

// The relay only ever gets `code`; `secret` stays on this device and goes into the fingerprint.
export function parsePairCode(input: string): PairCode | null {
  const fromUrl = input.match(/[#?&]pair=([^&\s]+)/i);
  let raw = fromUrl ? decodeURIComponent(fromUrl[1]) : input;
  raw = raw.replace(/[\s-]/g, "").toUpperCase().replace(/O/g, "0").replace(/[IL]/g, "1");
  if (!CROCKFORD.test(raw)) return null;
  return { code: `${raw.slice(0, 4)}-${raw.slice(4, 8)}`, secret: raw.slice(8) };
}

export function formatPairCode({ code, secret }: PairCode): string {
  return `${code}-${secret.slice(0, 4)}-${secret.slice(4)}`;
}

// Lookup, keygen, claim and derive. Throws ApiError with a code such as invalidCode, notFound, tooManyDevices or alreadyPaired.
export async function pairDevice(api: Api, input: string, deviceName: string, pairedKeys: readonly string[] = []): Promise<Pairing> {
  const parsed = parsePairCode(input);
  if (!parsed) throw new ApiError(400, "invalidCode");
  const { code, secret } = parsed;

  const { pluginPublicKey } = await api.lookupPairing(code);
  if (pairedKeys.includes(pluginPublicKey)) throw new ApiError(409, "alreadyPaired");
  const keys = await generateDeviceKey();
  const pluginPub = decode(pluginPublicKey);
  const devicePub = await exportPublicRaw(keys.publicKey);
  const aesKey = await deriveKey(keys.privateKey, pluginPub, devicePub);
  const print = await fingerprint(secret, pluginPub, devicePub);
  const devicePublicKey = encode(devicePub);
  const { deviceId, deviceToken } = await api.claimPairing(code, devicePublicKey, deviceName);
  return { deviceId, token: deviceToken, aesKey, pluginPublicKey, devicePublicKey, fingerprint: print };
}

export async function createSession(deps: SessionDeps): Promise<Session> {
  const sendTimeoutMs = deps.sendTimeoutMs ?? SEND_TIMEOUT_MS;
  const stateListeners = new Set<(s: SessionState) => void>();
  const messageListeners = new Set<(items: ChatItem[]) => void>();
  const pendingSends = new Map<string, (result: SendResult) => void>();
  const pendingTells = new Map<string, (result: SendResult) => void>();
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
    for (const finish of [...pendingSends.values(), ...pendingTells.values()]) finish({ ok: false, error: "offline" });
  }

  async function start() {
    stopConnection();
    pairing = await deps.store.getPairing();
    shownIds.clear();
    const [storedSettings, muted, channelPrefs, push, lastSeenWs, lastSeqSent, storedSyncTs, storedApproved] = await Promise.all([
      deps.store.getMeta("lastSettings"),
      deps.store.getMeta("mutedChannels"),
      deps.store.getMeta("channelPrefs"),
      deps.store.getMeta("pushEnabled"),
      deps.store.getMeta("lastSeenWs"),
      deps.store.getMeta("lastSeqSent"),
      deps.store.getMeta("syncTs"),
      deps.store.getMeta("approved"),
    ]);
    void deps.store.deleteMeta("tellPins").catch(() => undefined);
    guard = new SeqGuard(lastSeenWs);
    counter = new SeqCounter(lastSeqSent);
    // Stored values may predate payload validation.
    const settings = parseSettings(storedSettings);
    syncTs = isValidTs(storedSyncTs) ? storedSyncTs : 0;
    cacheLimit = deps.cacheLimit;
    approved = storedApproved;
    dropped = false;
    pluginOnline = undefined;

    if (!pairing) {
      setState(emptyState(state.status === "revoked" ? "revoked" : "unpaired", cacheLimit));
      return;
    }

    state = {
      ...emptyState(approved ? "connecting" : "pending", cacheLimit),
      fingerprint: pairing.fingerprint,
      character: settings?.character,
      characterWorld: settings?.characterWorld,
      relayChannels: settings?.relayChannels ?? [],
      sendChannels: settings?.sendChannels ?? [],
      maxLength: settings?.maxLength ?? DEFAULT_MAX_LENGTH,
      contacts: settings?.contacts ?? [],
      mutedChannels: muted,
      channelPrefs: withDefaults(channelPrefs),
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
    await deps.store.wipe();
    await deps.push.disable().catch(() => undefined);
    setState(emptyState("revoked", cacheLimit));
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
    void deps.store.setMeta("lastSeqSent", seq);
    const envelope = await sealPayload(pairing.aesKey, "d2p", { ...payload, seq });
    return connection?.send({ t: "send", payload: envelope }) ?? false;
  }

  async function sendHello() {
    backlogPending = true;
    deferredTs = 0;
    await sendPayload({ type: "hello", sinceTs: syncTs });
    await sendPrefs();
    await sendTellKey();
  }

  async function sendTellKey() {
    let tellKey = await deps.store.getMeta("tellKey");
    if (!tellKey) {
      const pair = await generateTellKey();
      tellKey = { privateKey: pair.privateKey, publicKey: encode(await exportPublicRaw(pair.publicKey)) };
      await deps.store.setMeta("tellKey", tellKey);
    }
    await sendPayload({ type: "tellKey", publicKey: tellKey.publicKey });
  }

  function sendPrefs() {
    return sendPayload({ type: "prefs", mutedChannels: state.mutedChannels, channels: channelNotifyPrefs(state.channelPrefs) });
  }

  async function markApproved() {
    if (approved) return;
    approved = true;
    await deps.store.setMeta("approved", true);
    refreshStatus();
    if (pluginOnline) await sendHello();
    // Permission is asked when Pair is tapped, so a new account starts with notifications on.
    await enablePush(false);
  }

  async function enablePush(prompt: boolean) {
    if (!pairing) return false;
    const enabled = await guarded(deps.push.enable(pairing.token, prompt)).catch(() => false);
    if (!pairing) return false;
    await deps.store.setMeta("pushEnabled", enabled);
    setState({ pushEnabled: enabled });
    return enabled;
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
      case "tell": {
        const item = pairing ? await openTellFrame(deps.store, frame.from, frame.id, frame.envelope, frame.fromKey) : null;
        if (item) await receiveItems([item], false);
        connection?.send({ t: "tellAck", ids: [frame.id] });
        return;
      }
      case "tellResult":
        pendingTells.get(frame.id)?.({ ok: frame.ok, ...(frame.error ? { error: frame.error } : {}) });
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
    await deps.store.setMeta("lastSeenWs", payload.seq);
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
        await deps.store.setMeta("lastSettings", payload);
        setState({
          character: payload.character,
          characterWorld: payload.characterWorld,
          relayChannels: payload.relayChannels,
          sendChannels: payload.sendChannels,
          maxLength: payload.maxLength,
          contacts: payload.contacts ?? [],
        });
        if (payload.character) await seedChannels(payload.character);
        return;
    }
  }

  async function seedChannels(character: string) {
    const prefs = withDefaultChannels(state.channelPrefs, character);
    if (prefs === state.channelPrefs) return;
    setState({ channelPrefs: prefs });
    await deps.store.setMeta("channelPrefs", prefs);
  }

  async function sendRelayedTell(contact: TellContact, text: string): Promise<SendResult> {
    if (!pairing) return { ok: false, error: "offline" };
    const { token, deviceId, pluginPublicKey } = pairing;
    const issued = await deps.store.getMeta("tellBundles");
    const isStale = (bundle: TellBundle) => Object.hasOwn(issued, bundle.installPublicKey) && bundle.issuedAt < issued[bundle.installPublicKey];
    let recipient: TellBundle | null;
    let own: TellBundle | null;
    try {
      const [theirs, mine] = await Promise.all([
        deps.api.getTellBundle(token, contact.installId),
        deps.api.getTellBundle(token, "self").catch(() => null),
      ]);
      recipient = await verifyBundle(theirs, contact.key);
      if (!recipient) return { ok: false, error: (await verifyBundle(theirs)) ? "keyChanged" : "notChatTerror" };
      if (isStale(recipient)) return { ok: false, error: "staleBundle" };
      own = mine && (await verifyBundle(mine, pluginPublicKey));
      if (own && isStale(own)) own = null;
    } catch (error) {
      return { ok: false, error: error instanceof ApiError && error.status === 404 ? "notChatTerror" : "offline" };
    }
    await deps.store.setMeta("tellBundles", { ...issued, [recipient.installPublicKey]: recipient.issuedAt, ...(own ? { [own.installPublicKey]: own.issuedAt } : {}) });

    const body: TellBody = {
      id: crypto.randomUUID().replace(/-/g, ""),
      fromHash: contact.characterHash,
      fromName: contact.character,
      fromWorld: contact.characterWorld,
      toHash: contact.hash,
      toName: contact.name,
      toWorld: contact.world,
      text,
      ts: Date.now(),
    };
    const copies = await buildCopies(body, recipient, own, deviceId);
    return new Promise((resolve) => {
      const finish = (result: SendResult) => {
        clearTimeout(timer);
        pendingTells.delete(body.id);
        const item = result.ok ? tellToItem(body, state.contacts, true) : null;
        if (item) void receiveItems([item], false);
        resolve(result);
      };
      const timer = setTimeout(() => finish({ ok: false, error: "timeout" }), sendTimeoutMs);
      pendingTells.set(body.id, finish);
      if (!connection?.send({ t: "tellSend", id: body.id, to: contact.installId, copies })) finish({ ok: false, error: "offline" });
    });
  }

  async function advanceSync(ts: number) {
    if (ts <= syncTs) return;
    syncTs = ts;
    await deps.store.setMeta("syncTs", ts);
  }

  async function receiveItems(items: ChatItem[], moveSync: boolean) {
    if (items.length === 0) return;
    const sorted = [...items].sort((a, b) => a.ts - b.ts);
    await deps.store.addMessages(sorted, cacheLimit);
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

  const session: Session = {
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
      const items = await deps.store.loadMessages(limit);
      for (const item of items) shownIds.add(item.id);
      return items;
    },

    send(channel, text, target, character) {
      if (!authed || !approved) return Promise.resolve({ ok: false, error: "offline" });
      if (pluginOnline === false) {
        // Tells to ChatTerror friends still go out through the relay while the game is closed.
        const contact = channel === "tell" && target ? findContact(state.contacts, target, character) : undefined;
        return contact ? sendRelayedTell(contact, text) : Promise.resolve({ ok: false, error: "gameOffline" });
      }
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
      await deps.store.setMeta("mutedChannels", [...channels]);
      await sendPrefs();
    },

    async setChannelPrefs(prefs) {
      setState({ channelPrefs: prefs });
      await deps.store.setMeta("channelPrefs", prefs);
      await sendPrefs();
    },

    enablePush: () => enablePush(true),

    async disablePush() {
      if (!pairing) return;
      await guarded(deps.push.disable(pairing.token)).catch(() => undefined);
      if (!pairing) return;
      await deps.store.setMeta("pushEnabled", false);
      setState({ pushEnabled: false });
    },

    clearCache: () => deps.store.clearMessages(),

    async setCacheLimit(n) {
      cacheLimit = n;
      setState({ cacheLimit: n });
      await deps.store.trimMessages(n);
    },

    async unpair() {
      if (pairing) await deps.api.deleteDevice(pairing.token, pairing.deviceId).catch(() => undefined);
      await deps.push.disable().catch(() => undefined);
      stopConnection();
      pairing = undefined;
      await deps.store.wipe();
      setState(emptyState("unpaired", cacheLimit));
    },

    close() {
      stopConnection();
      stateListeners.clear();
      messageListeners.clear();
    },
  };

  await start();
  return session;
}
