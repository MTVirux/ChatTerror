import { EMPTY_CHANNEL_PREFS, type ChannelPrefs } from "./channelPrefs";
import { isChatItem, type ChatChannel, type ChatItem, type SettingsPayload } from "./protocol";

export interface Pairing {
  deviceId: string;
  token: string;
  aesKey: CryptoKey;
  pluginPublicKey: string;
  devicePublicKey: string;
  fingerprint: string;
}

export interface Meta {
  lastSeenWs: number;
  lastSeenPush: number;
  lastSeqSent: number;
  // Newest ts received over the socket. Push-only messages do not count, so hello still backfills around them.
  syncTs: number;
  mutedChannels: ChatChannel[];
  channelPrefs: ChannelPrefs;
  // Only read when adopting a database from before multiple accounts.
  cacheLimit: number;
  lastSettings: SettingsPayload | null;
  approved: boolean;
  pushEnabled: boolean;
  // Key for relayed tells, separate from the pairing key whose private half is not kept.
  tellKey: { privateKey: CryptoKey; publicKey: string } | null;
  // Install key first seen for each friend's character hash.
  tellPins: Record<string, string>;
  // Newest tell bundle issuedAt accepted per install key, so the relay can't hand out an older bundle.
  tellBundles: Record<string, number>;
}

export const DEFAULT_CACHE_LIMIT = 2000;

const META_DEFAULTS: Meta = {
  lastSeenWs: 0,
  lastSeenPush: 0,
  lastSeqSent: 0,
  syncTs: 0,
  mutedChannels: [],
  channelPrefs: EMPTY_CHANNEL_PREFS,
  cacheLimit: DEFAULT_CACHE_LIMIT,
  lastSettings: null,
  approved: false,
  pushEnabled: false,
  tellKey: null,
  tellPins: {},
  tellBundles: {},
};

const STORES = ["pairing", "messages", "meta"] as const;
type StoreName = (typeof STORES)[number];

export const LEGACY_DB_NAME = "chatterror";

export function accountDbName(deviceId: string): string {
  return `chatterror-${deviceId}`;
}

// Without this, mobile browsers may evict IndexedDB and lose the pairing.
export function requestPersistentStorage(): void {
  if (typeof navigator === "undefined" || !navigator.storage?.persist) return;
  navigator.storage
    .persisted()
    .then((persisted) => persisted || navigator.storage.persist())
    .catch(() => {});
}

const databases = new Map<string, Promise<IDBDatabase>>();

function openDb(name: string): Promise<IDBDatabase> {
  let promise = databases.get(name);
  if (!promise) {
    promise = new Promise((resolve, reject) => {
      const request = indexedDB.open(name, 1);
      request.onupgradeneeded = () => {
        const db = request.result;
        db.createObjectStore("pairing");
        db.createObjectStore("meta");
        db.createObjectStore("messages", { keyPath: "id" }).createIndex("ts", "ts");
      };
      request.onsuccess = () => {
        const db = request.result;
        // Removing an account from another tab or the service worker must not be blocked by this connection.
        db.onversionchange = () => {
          db.close();
          databases.delete(name);
        };
        resolve(db);
      };
      request.onerror = () => {
        databases.delete(name);
        reject(request.error);
      };
    });
    databases.set(name, promise);
  }
  return promise;
}

export function resetStorageForTests(): void {
  databases.clear();
}

function result<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function done(tx: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
  });
}

async function trimIn(store: IDBObjectStore, limit: number): Promise<void> {
  let excess = (await result(store.count())) - limit;
  if (excess <= 0) return;
  const cursorRequest = store.index("ts").openCursor();
  await new Promise<void>((resolve, reject) => {
    cursorRequest.onsuccess = () => {
      const cursor = cursorRequest.result;
      if (!cursor || excess <= 0) return resolve();
      cursor.delete();
      excess--;
      cursor.continue();
    };
    cursorRequest.onerror = () => reject(cursorRequest.error);
  });
}

export interface AccountStore {
  readonly dbName: string;
  getPairing(): Promise<Pairing | undefined>;
  setPairing(pairing: Pairing): Promise<void>;
  getMeta<K extends keyof Meta>(key: K): Promise<Meta[K]>;
  setMeta<K extends keyof Meta>(key: K, value: Meta[K]): Promise<void>;
  addMessages(items: ChatItem[], limit: number): Promise<ChatItem[]>;
  trimMessages(limit: number): Promise<void>;
  loadMessages(limit: number): Promise<ChatItem[]>;
  clearMessages(): Promise<void>;
  wipe(): Promise<void>;
  destroy(): Promise<void>;
}

export function openAccountStore(dbName: string): AccountStore {
  async function run<T>(stores: StoreName | StoreName[], mode: IDBTransactionMode, work: (tx: IDBTransaction) => Promise<T>): Promise<T> {
    const tx = (await openDb(dbName)).transaction(stores, mode);
    const finished = done(tx);
    const value = await work(tx);
    await finished;
    return value;
  }

  return {
    dbName,
    getPairing: () => run("pairing", "readonly", (tx) => result(tx.objectStore("pairing").get("current"))),
    setPairing: (pairing) => run("pairing", "readwrite", async (tx) => {
      await result(tx.objectStore("pairing").put(pairing, "current"));
    }),
    async getMeta(key) {
      const value = await run("meta", "readonly", (tx) => result(tx.objectStore("meta").get(key)));
      return value === undefined ? META_DEFAULTS[key] : value;
    },
    setMeta: (key, value) => run("meta", "readwrite", async (tx) => {
      await result(tx.objectStore("meta").put(value, key));
    }),
    addMessages: (items, limit) => run("messages", "readwrite", async (tx) => {
      const store = tx.objectStore("messages");
      const added: ChatItem[] = [];
      const ids = new Set<string>();
      for (const item of items) {
        if (ids.has(item.id)) continue;
        ids.add(item.id);
        if ((await result(store.getKey(item.id))) !== undefined) continue;
        store.put(item);
        added.push(item);
      }
      await trimIn(store, limit);
      return added;
    }),
    trimMessages: (limit) => run("messages", "readwrite", (tx) => trimIn(tx.objectStore("messages"), limit)),
    loadMessages: (limit) => run("messages", "readonly", (tx) => {
      const request = tx.objectStore("messages").index("ts").openCursor(null, "prev");
      const items: ChatItem[] = [];
      return new Promise((resolve, reject) => {
        request.onsuccess = () => {
          const cursor = request.result;
          if (!cursor || items.length >= limit) return resolve(items.reverse());
          // Items stored before validation existed may be malformed.
          if (isChatItem(cursor.value)) items.push(cursor.value);
          cursor.continue();
        };
        request.onerror = () => reject(request.error);
      });
    }),
    clearMessages: () => run("messages", "readwrite", async (tx) => {
      await result(tx.objectStore("messages").clear());
    }),
    wipe: () => run([...STORES], "readwrite", async (tx) => {
      await Promise.all(STORES.map((name) => result(tx.objectStore(name).clear())));
    }),
    async destroy() {
      const db = await databases.get(dbName)?.catch(() => undefined);
      db?.close();
      databases.delete(dbName);
      await new Promise<void>((resolve, reject) => {
        const request = indexedDB.deleteDatabase(dbName);
        request.onsuccess = () => resolve();
        request.onerror = () => reject(request.error);
        // Deletion finishes once the other connections close; nothing here depends on it.
        request.onblocked = () => resolve();
      });
    },
  };
}
