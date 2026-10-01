import type { AccountView, FeedItem } from "../core/accounts";
import { tellPartner } from "./format";

export function feedKey(item: FeedItem): string {
  return `${item.deviceId}:${item.id}`;
}

export function mergeFeed(existing: FeedItem[], incoming: FeedItem[], max: number): FeedItem[] {
  const seen = new Set(existing.map(feedKey));
  const fresh = incoming.filter((i) => !seen.has(feedKey(i)) && seen.add(feedKey(i)));
  if (fresh.length === 0) return existing;
  const out = existing.concat(fresh).sort((a, b) => a.ts - b.ts);
  return out.length > max ? out.slice(-max) : out;
}

function lastTellWith(items: FeedItem[], partner: string): FeedItem | undefined {
  for (let i = items.length - 1; i >= 0; i--) {
    if (items[i].channel === "tell" && tellPartner(items[i]) === partner) return items[i];
  }
  return undefined;
}

export function defaultSendAccount(items: FeedItem[], accounts: AccountView[], current: string | undefined, partner?: string): string | undefined {
  const active = accounts.filter((a) => a.status === "active");
  if (partner) {
    const last = lastTellWith(items, partner);
    if (last && active.some((a) => a.deviceId === last.deviceId)) return last.deviceId;
  }
  if (current && active.some((a) => a.deviceId === current && a.state.status === "online")) return current;
  return (active.find((a) => a.state.status === "online") ?? active[0])?.deviceId;
}
