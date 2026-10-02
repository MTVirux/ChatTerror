import { DEFAULT_CACHE_LIMIT, LEGACY_DB_NAME, openAccountStore } from "./storage";

export type AccountStatus = "active" | "revoked";

export interface AccountRecord {
  deviceId: string;
  dbName: string;
  pluginPublicKey: string;
  label: string;
  order: number;
  addedAt: number;
  status: AccountStatus;
  name?: string;
}

let registryPromise: Promise<IDBDatabase> | undefined;
let adoption: Promise<void> | undefined;

function openRegistry(): Promise<IDBDatabase> {
  registryPromise ??= new Promise((resolve, reject) => {
    const request = indexedDB.open("chatterror-accounts", 1);
    request.onupgradeneeded = () => {
      request.result.createObjectStore("accounts", { keyPath: "deviceId" });
      request.result.createObjectStore("prefs");
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
  return registryPromise;
}

export function resetRegistryForTests(): void {
  registryPromise = undefined;
  adoption = undefined;
}

function result<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function run<T>(mode: IDBTransactionMode, work: (accounts: IDBObjectStore, prefs: IDBObjectStore) => Promise<T>): Promise<T> {
  const tx = (await openRegistry()).transaction(["accounts", "prefs"], mode);
  const finished = new Promise<void>((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
  });
  const value = await work(tx.objectStore("accounts"), tx.objectStore("prefs"));
  await finished;
  return value;
}

// The database from before multiple accounts becomes the first account in place, so nothing is copied.
async function adoptLegacy(): Promise<void> {
  if (await run("readonly", (_, prefs) => result(prefs.get("legacyAdopted")))) return;
  const legacy = openAccountStore(LEGACY_DB_NAME);
  const pairing = await legacy.getPairing();
  const cacheLimit = await legacy.getMeta("cacheLimit");
  const label = (await legacy.getMeta("lastSettings"))?.character ?? "";
  await run("readwrite", async (accounts, prefs) => {
    if (await result(prefs.get("legacyAdopted"))) return;
    if (pairing && (await result(accounts.get(pairing.deviceId))) === undefined) {
      const record: AccountRecord = {
        deviceId: pairing.deviceId,
        dbName: LEGACY_DB_NAME,
        pluginPublicKey: pairing.pluginPublicKey,
        label,
        order: 0,
        addedAt: Date.now(),
        status: "active",
      };
      accounts.put(record);
    }
    if ((await result(prefs.get("cacheLimit"))) === undefined) prefs.put(cacheLimit, "cacheLimit");
    prefs.put(true, "legacyAdopted");
  });
}

export async function listAccounts(): Promise<AccountRecord[]> {
  adoption ??= adoptLegacy().catch((error) => {
    adoption = undefined;
    throw error;
  });
  await adoption;
  const all = await run("readonly", (accounts) => result(accounts.getAll() as IDBRequest<AccountRecord[]>));
  return all.sort((a, b) => a.order - b.order);
}

export async function findAccount(deviceId: string): Promise<AccountRecord | undefined> {
  return (await listAccounts()).find((a) => a.deviceId === deviceId);
}

export async function addAccount(record: Omit<AccountRecord, "order">): Promise<AccountRecord> {
  await listAccounts();
  return run("readwrite", async (accounts) => {
    const existing = await result(accounts.getAll() as IDBRequest<AccountRecord[]>);
    const full = { ...record, order: existing.reduce((max, a) => Math.max(max, a.order + 1), 0) };
    accounts.put(full);
    return full;
  });
}

export function updateAccount(deviceId: string, patch: Partial<Pick<AccountRecord, "label" | "status" | "name">>): Promise<void> {
  return run("readwrite", async (accounts) => {
    const current = await result(accounts.get(deviceId) as IDBRequest<AccountRecord | undefined>);
    if (current) accounts.put({ ...current, ...patch });
  });
}

export function removeAccount(deviceId: string): Promise<void> {
  return run("readwrite", async (accounts) => {
    await result(accounts.delete(deviceId));
  });
}

export async function getCacheLimit(): Promise<number> {
  await listAccounts();
  const value = await run("readonly", (_, prefs) => result(prefs.get("cacheLimit")));
  return typeof value === "number" ? value : DEFAULT_CACHE_LIMIT;
}

export function setCacheLimit(n: number): Promise<void> {
  return run("readwrite", async (_, prefs) => {
    await result(prefs.put(n, "cacheLimit"));
  });
}
