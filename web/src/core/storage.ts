import type { ChatChannel, ChatItem, SettingsPayload } from "./protocol";

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
  cacheLimit: number;
  lastSettings: SettingsPayload | null;
  approved: boolean;
  pushEnabled: boolean;
}

export const DEFAULT_CACHE_LIMIT = 2000;

const META_DEFAULTS: Meta = {
  lastSeenWs: 0,
  lastSeenPush: 0,
  lastSeqSent: 0,
  syncTs: 0,
  mutedChannels: [],
  cacheLimit: DEFAULT_CACHE_LIMIT,
  lastSettings: null,
  approved: false,
  pushEnabled: false,
};

const STORES = ["pairing", "messages", "meta"] as const;
type StoreName = (typeof STORES)[number];

let dbPromise: Promise<IDBDatabase> | undefined;

function openDb(): Promise<IDBDatabase> {
  dbPromise ??= new Promise((resolve, reject) => {
    const request = indexedDB.open("chatterror", 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      db.createObjectStore("pairing");
      db.createObjectStore("meta");
      db.createObjectStore("messages", { keyPath: "id" }).createIndex("ts", "ts");
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
  return dbPromise;
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

async function run<T>(stores: StoreName | StoreName[], mode: IDBTransactionMode, work: (tx: IDBTransaction) => Promise<T>): Promise<T> {
  const tx = (await openDb()).transaction(stores, mode);
  const finished = done(tx);
  const value = await work(tx);
  await finished;
  return value;
}

export function getPairing(): Promise<Pairing | undefined> {
  return run("pairing", "readonly", (tx) => result(tx.objectStore("pairing").get("current")));
}

export function setPairing(pairing: Pairing): Promise<void> {
  return run("pairing", "readwrite", async (tx) => {
    await result(tx.objectStore("pairing").put(pairing, "current"));
  });
}

export async function getMeta<K extends keyof Meta>(key: K): Promise<Meta[K]> {
  const value = await run("meta", "readonly", (tx) => result(tx.objectStore("meta").get(key)));
  return value === undefined ? META_DEFAULTS[key] : value;
}

export function setMeta<K extends keyof Meta>(key: K, value: Meta[K]): Promise<void> {
  return run("meta", "readwrite", async (tx) => {
    await result(tx.objectStore("meta").put(value, key));
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

export function addMessages(items: ChatItem[], limit: number): Promise<ChatItem[]> {
  return run("messages", "readwrite", async (tx) => {
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
  });
}

export function trimMessages(limit: number): Promise<void> {
  return run("messages", "readwrite", (tx) => trimIn(tx.objectStore("messages"), limit));
}

export function loadMessages(limit: number): Promise<ChatItem[]> {
  return run("messages", "readonly", (tx) => {
    const request = tx.objectStore("messages").index("ts").openCursor(null, "prev");
    const items: ChatItem[] = [];
    return new Promise((resolve, reject) => {
      request.onsuccess = () => {
        const cursor = request.result;
        if (!cursor || items.length >= limit) return resolve(items.reverse());
        items.push(cursor.value as ChatItem);
        cursor.continue();
      };
      request.onerror = () => reject(request.error);
    });
  });
}

export function clearMessages(): Promise<void> {
  return run("messages", "readwrite", async (tx) => {
    await result(tx.objectStore("messages").clear());
  });
}

export function wipeAll(): Promise<void> {
  return run([...STORES], "readwrite", async (tx) => {
    await Promise.all(STORES.map((name) => result(tx.objectStore(name).clear())));
  });
}
