import type { FeedItem } from "../core/accounts";

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
