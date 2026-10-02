import { uniqueLabels } from "./accountIdentity";
import { openPayload } from "./crypto";
import { CHANNEL_LABELS, parsePluginPayload, type ChatItem } from "./protocol";
import { getCacheLimit, listAccounts } from "./registry";
import { SeqGuard } from "./seq";
import { openAccountStore } from "./storage";

export interface RoutedPush {
  deviceId: string;
  label: string;
  multiple: boolean;
  item: ChatItem;
}

export async function routePush(body: unknown): Promise<RoutedPush | null> {
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
