import { accountColors, uniqueLabels } from "./accountIdentity";
import { createApi, type Api } from "./api";
import type { ChatItem } from "./protocol";
import { createPushControl, registerServiceWorker, type PushControl } from "./push";
import { connectRelay } from "./relay";
import * as registry from "./registry";
import type { AccountRecord, AccountStatus } from "./registry";
import { createSession, pairDevice, type Session, type SessionDeps, type SessionState } from "./session";
import { accountDbName, openAccountStore, requestPersistentStorage, type AccountStore } from "./storage";

export interface AccountView {
  deviceId: string;
  label: string;
  color: string;
  character?: string;
  status: AccountStatus;
  state: SessionState;
  unread: number;
}

export interface FeedItem extends ChatItem {
  deviceId: string;
}

export type Viewing = "all" | string | null;

export interface ManagerDeps {
  api: Api;
  connect: SessionDeps["connect"];
  push: PushControl;
}

export interface AccountManager {
  list(): AccountView[];
  subscribe(cb: (accounts: AccountView[]) => void): () => void;
  session(deviceId: string): Session | undefined;
  onMessages(cb: (items: FeedItem[]) => void): () => void;
  loadMerged(limit: number): Promise<FeedItem[]>;
  pair(code: string, deviceName: string): Promise<{ deviceId: string; fingerprint: string }>;
  remove(deviceId: string): Promise<void>;
  rename(deviceId: string, name: string): Promise<void>;
  setViewing(viewing: Viewing): void;
  cacheLimit(): number;
  setCacheLimit(n: number): Promise<void>;
  close(): void;
}

interface Entry {
  record: AccountRecord;
  store: AccountStore;
  session: Session;
  unread: number;
  offState: () => void;
  offMessages: () => void;
}

export async function createAccountManager(deps: ManagerDeps): Promise<AccountManager> {
  const entries = new Map<string, Entry>();
  const listeners = new Set<(accounts: AccountView[]) => void>();
  const messageListeners = new Set<(items: FeedItem[]) => void>();
  let viewing: Viewing = null;
  let cacheLimit = await registry.getCacheLimit();
  let views: AccountView[] = [];

  function ordered(): Entry[] {
    return [...entries.values()].sort((a, b) => a.record.order - b.record.order);
  }

  function emit() {
    const list = ordered();
    const labels = uniqueLabels(list.map((e, i) => ({
      name: e.record.name,
      fallback: String(e.session.getState().character || e.record.label || `Account ${i + 1}`),
    })));
    const colors = accountColors(list.map((e) => e.record.deviceId));
    views = list.map((e, i) => {
      const state = e.session.getState();
      return {
        deviceId: e.record.deviceId,
        label: labels[i],
        color: colors[i],
        character: state.character,
        status: e.record.status,
        state,
        unread: e.unread,
      };
    });
    for (const listener of listeners) listener(views);
  }

  // Only the last account using push may drop the browser subscription, the others share it.
  function pushFor(deviceId: string): PushControl {
    return {
      enable: (token, prompt) => deps.push.enable(token, prompt),
      async disable(token) {
        const shared = ordered().some((e) => e.record.deviceId !== deviceId && e.session.getState().pushEnabled);
        if (!shared) return deps.push.disable(token);
        if (token) await deps.api.deletePush(token);
      },
    };
  }

  function onState(entry: Entry, state: SessionState) {
    if (state.status === "revoked" && entry.record.status !== "revoked") {
      entry.record = { ...entry.record, status: "revoked" };
      void registry.updateAccount(entry.record.deviceId, { status: "revoked" });
    }
    if (state.character && state.character !== entry.record.label) {
      entry.record = { ...entry.record, label: state.character };
      void registry.updateAccount(entry.record.deviceId, { label: state.character });
    }
    emit();
  }

  function onItems(entry: Entry, items: ChatItem[]) {
    const id = entry.record.deviceId;
    if (viewing !== "all" && viewing !== id) {
      const incoming = items.filter((i) => !i.outgoing).length;
      if (incoming > 0) {
        entry.unread += incoming;
        emit();
      }
    }
    const tagged = items.map((i) => ({ ...i, deviceId: id }));
    for (const listener of messageListeners) listener(tagged);
  }

  async function open(record: AccountRecord): Promise<Entry> {
    const store = openAccountStore(record.dbName);
    const session = await createSession({ api: deps.api, connect: deps.connect, push: pushFor(record.deviceId), store, cacheLimit });
    const entry = { record, store, session, unread: 0 } as Entry;
    entry.offState = session.subscribe((s) => onState(entry, s));
    entry.offMessages = session.onMessages((items) => onItems(entry, items));
    entries.set(record.deviceId, entry);
    return entry;
  }

  async function drop(entry: Entry) {
    entry.offState();
    entry.offMessages();
    entry.session.close();
    entries.delete(entry.record.deviceId);
    await registry.removeAccount(entry.record.deviceId);
    await entry.store.destroy();
  }

  for (const record of await registry.listAccounts()) {
    try {
      const entry = await open(record);
      if (record.status === "active" && entry.session.getState().status === "unpaired") await drop(entry);
    } catch {
      // Skip just this account; its registry row stays so it can load on the next start.
    }
  }
  emit();

  return {
    list: () => views,

    subscribe(cb) {
      listeners.add(cb);
      return () => listeners.delete(cb);
    },

    session: (deviceId) => entries.get(deviceId)?.session,

    onMessages(cb) {
      messageListeners.add(cb);
      return () => messageListeners.delete(cb);
    },

    async loadMerged(limit) {
      const lists = await Promise.all(
        ordered().map(async (e) => (await e.session.loadHistory(limit)).map((i) => ({ ...i, deviceId: e.record.deviceId }))),
      );
      return lists.flat().sort((a, b) => a.ts - b.ts).slice(-limit);
    },

    async pair(code, deviceName) {
      const active = ordered().filter((e) => e.record.status === "active").map((e) => e.record.pluginPublicKey);
      const pairing = await pairDevice(deps.api, code, deviceName, active);
      const store = openAccountStore(accountDbName(pairing.deviceId));
      await store.wipe();
      await store.setPairing(pairing);
      // Pairing again after a revoke replaces the revoked tab.
      for (const old of ordered().filter((e) => e.record.status === "revoked" && e.record.pluginPublicKey === pairing.pluginPublicKey)) {
        await drop(old);
      }
      const record = await registry.addAccount({
        deviceId: pairing.deviceId,
        dbName: store.dbName,
        pluginPublicKey: pairing.pluginPublicKey,
        label: "",
        addedAt: Date.now(),
        status: "active",
      });
      await open(record);
      emit();
      requestPersistentStorage();
      return { deviceId: pairing.deviceId, fingerprint: pairing.fingerprint };
    },

    async remove(deviceId) {
      const entry = entries.get(deviceId);
      if (!entry) return;
      if (entry.record.status === "active") await entry.session.unpair();
      await drop(entry);
      emit();
    },

    async rename(deviceId, name) {
      const entry = entries.get(deviceId);
      if (!entry) return;
      const trimmed = name.trim() || undefined;
      entry.record = { ...entry.record, name: trimmed };
      await registry.updateAccount(deviceId, { name: trimmed });
      emit();
    },

    setViewing(next) {
      viewing = next;
      let changed = false;
      for (const e of entries.values()) {
        if ((next === "all" || next === e.record.deviceId) && e.unread > 0) {
          e.unread = 0;
          changed = true;
        }
      }
      if (changed) emit();
    },

    cacheLimit: () => cacheLimit,

    async setCacheLimit(n) {
      cacheLimit = n;
      await registry.setCacheLimit(n);
      await Promise.all(ordered().map((e) => e.session.setCacheLimit(n)));
      emit();
    },

    close() {
      for (const e of entries.values()) {
        e.offState();
        e.offMessages();
        e.session.close();
      }
      entries.clear();
      listeners.clear();
      messageListeners.clear();
    },
  };
}

export async function openAccounts(): Promise<AccountManager> {
  void registerServiceWorker();
  const api = createApi();
  return createAccountManager({ api, connect: connectRelay, push: createPushControl(api) });
}
