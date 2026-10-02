import { uniqueLabels } from "./accountIdentity";
import { openPayload } from "./crypto";
import { CHANNEL_LABELS, parsePluginPayload, type ChatItem } from "./protocol";
import { getCacheLimit, listAccounts } from "./registry";
import { SeqGuard } from "./seq";
import { openAccountStore } from "./storage";
import { openTellFrame } from "./tells";

export interface RoutedPush {
  deviceId: string;
  label: string;
  multiple: boolean;
  item: ChatItem;
}

async function routeTell(tell: { i?: unknown; f?: unknown; e?: unknown; d?: unknown }): Promise<RoutedPush | null> {
  if (typeof tell.i !== "string" || typeof tell.f !== "string" || typeof tell.e !== "string") return null;
  const accounts = (await listAccounts()).filter((a) => a.status === "active");
  const account = accounts.find((a) => a.deviceId === tell.d);
  if (!account) return null;
  const store = openAccountStore(account.dbName);
  const item = await openTellFrame(store, tell.f, tell.i, tell.e);
  if (!item) return null;
  await store.addMessages([item], await getCacheLimit());
  const index = accounts.indexOf(account);
  return { deviceId: account.deviceId, label: account.name || account.label || `Account ${index + 1}`, multiple: accounts.length > 1, item };
}

export async function routePush(body: unknown): Promise<RoutedPush | null> {
  const tell = (body ?? {}) as { t?: unknown; i?: unknown; f?: unknown; e?: unknown; d?: unknown };
  if (tell.t === "tell") return routeTell(tell);
  const { p, d } = (body ?? {}) as { p?: unknown; d?: unknown };
  if (typeof p !== "string") return null;
  const all = await listAccounts();
  const labels = uniqueLabels(all.map((a, i) => ({ name: a.name, fallback: a.label || `Account ${i + 1}` })));
  const accounts = all.filter((a) => a.status === "active");
  const matching = accounts.filter((a) => a.deviceId === d);
  // Older relays send no device id, so every key is tried.
  for (const account of matching.length > 0 ? matching : accounts) {
    const store = openAccountStore(account.dbName);
    const pairing = await store.getPairing();
    if (!pairing) continue;
    let payload;
    try {
      payload = parsePluginPayload(await openPayload(pairing.aesKey, "p2d", p));
    } catch {
      continue;
    }
    if (payload?.type !== "chat") return null;
    const guard = new SeqGuard(await store.getMeta("lastSeenPush"));
    if (!guard.accept(payload.seq)) return null;
    await store.setMeta("lastSeenPush", payload.seq);
    await store.addMessages([payload.item], await getCacheLimit());
    return { deviceId: account.deviceId, label: labels[all.indexOf(account)], multiple: accounts.length > 1, item: payload.item };
  }
  return null;
}

export function notificationTitle({ label, multiple, item }: RoutedPush): string {
  const base = `${item.sender} (${CHANNEL_LABELS[item.channel]})`;
  return multiple ? `${label} - ${base}` : base;
}
