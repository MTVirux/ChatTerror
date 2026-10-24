import type { FeedItem } from "../core/accounts";

export function feedKey(item: FeedItem): string {
  return `${item.deviceId}:${item.id}`;
}

export function mergeFeed(existing: FeedItem[], incoming: FeedItem[], maxPerKey: number, keyOf: (item: FeedItem) => string): FeedItem[] {
  const seen = new Set(existing.map(feedKey));
  const fresh = incoming.filter((i) => !seen.has(feedKey(i)) && seen.add(feedKey(i)));
  if (fresh.length === 0) return existing;
  const out = existing.concat(fresh).sort((a, b) => a.ts - b.ts);
  const counts = new Map<string, number>();
  const kept = out.reverse().filter((item) => {
    const key = keyOf(item);
    const count = (counts.get(key) ?? 0) + 1;
    counts.set(key, count);
    return count <= maxPerKey;
  });
  return kept.reverse();
}
