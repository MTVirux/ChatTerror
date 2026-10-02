import { uniqueLabels } from "./accountIdentity";
import { openPayload } from "./crypto";
import { CHANNEL_LABELS, parsePluginPayload, type ChatItem } from "./protocol";
import { getCacheLimit, listAccounts, type AccountRecord } from "./registry";
import { SeqGuard } from "./seq";
import { openAccountStore } from "./storage";
import { openTellFrame } from "./tells";

export interface RoutedPush {
  deviceId: string;
  label: string;
  multiple: boolean;
  item: ChatItem;
}

async function activeAccounts() {
  const all = await listAccounts();
  const labels = uniqueLabels(all.map((a, i) => ({ name: a.name, fallback: a.label || `Account ${i + 1}` })));
  const accounts = all.filter((a) => a.status === "active");
  return { accounts, labelOf: (account: AccountRecord) => labels[all.indexOf(account)] };
}

async function routeTell(tell: { i?: unknown; f?: unknown; e?: unknown; k?: unknown; d?: unknown }): Promise<RoutedPush | "duplicate" | null> {
  if (typeof tell.i !== "string" || typeof tell.f !== "string" || typeof tell.e !== "string" || typeof tell.k !== "string") return null;
  const { accounts, labelOf } = await activeAccounts();
  const account = accounts.find((a) => a.deviceId === tell.d);
  if (!account) return null;
  const store = openAccountStore(account.dbName);
  const item = await openTellFrame(store, tell.f, tell.i, tell.e, tell.k);
  if (!item) return null;
  const added = await store.addMessages([item], await getCacheLimit());
  if (added.length === 0) return "duplicate";
  return { deviceId: account.deviceId, label: labelOf(account), multiple: accounts.length > 1, item };
}

// "duplicate" is a tell already stored, which must not notify again.
export async function routePush(body: unknown): Promise<RoutedPush | "duplicate" | null> {
  const tell = (body ?? {}) as { t?: unknown; i?: unknown; f?: unknown; e?: unknown; k?: unknown; d?: unknown };
  if (tell.t === "tell") return routeTell(tell);
  const { p, d } = (body ?? {}) as { p?: unknown; d?: unknown };
  if (typeof p !== "string") return null;
  const { accounts, labelOf } = await activeAccounts();
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
    return { deviceId: account.deviceId, label: labelOf(account), multiple: accounts.length > 1, item: payload.item };
  }
  return null;
}

export function notificationTitle({ label, multiple, item }: RoutedPush): string {
  const base = `${item.sender} (${CHANNEL_LABELS[item.channel]})`;
  return multiple ? `${label} - ${base}` : base;
}
