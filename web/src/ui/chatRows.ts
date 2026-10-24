import type { FeedItem } from "../core/accounts";
import { channelIncludes, customKey, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import { itemKey, partnersOf } from "./channels";

// One chat in a character's list: a tell partner or a group.
export interface ChatRow {
  key: string;
  kind: "dm" | "group";
  character: string;
  // The partner's name without the world, or the group name.
  name: string;
  // "Name Surname@World", or "Name Surname" when the world is unknown.
  partner?: string;
  world?: string;
  // Without "tell", which groups never show.
  custom?: CustomChannel;
  last?: FeedItem;
  pinned: boolean;
  muted: boolean;
}

// The character's groups in creation order. A group left with only tells, like the old Tells channel, is hidden.
export function visibleGroups(prefs: ChannelPrefs, character: string): CustomChannel[] {
  return prefs.custom.flatMap((c) => {
    if (c.character !== character) return [];
    const channels = c.channels.filter((ch) => ch !== "tell");
    return channels.length > 0 ? [{ ...c, channels }] : [];
  });
}

export function dmRow(prefs: ChannelPrefs, character: string, partner: string, last?: FeedItem): ChatRow {
  const key = `t|${character}|${partner}`;
  const [name, world] = partner.split("@");
  return { key, kind: "dm", character, name, partner, world, last, pinned: prefs.pinned.includes(key), muted: prefs.muted.includes(key) };
}

function groupRow(prefs: ChannelPrefs, custom: CustomChannel, last?: FeedItem): ChatRow {
  const key = customKey(custom);
  return { key, kind: "group", character: custom.character, name: custom.name, custom, last, pinned: prefs.pinned.includes(key), muted: prefs.muted.includes(key) };
}

function dmPartner(key: string, character: string): string | undefined {
  const [kind, c, partner] = key.split("|");
  return kind === "t" && c === character && partner ? partner : undefined;
}

// Pins first in pin order, then newest message first, then groups with no messages in creation order.
export function chatRows(prefs: ChannelPrefs, character: string, items: FeedItem[]): ChatRow[] {
  // Items come oldest first, so the last one per key is its newest.
  const newest = new Map<string, FeedItem>();
  for (const item of items) newest.set(itemKey(item), item);
  const lastOf = (key: string) => {
    let last: FeedItem | undefined;
    for (const [k, item] of newest) if (channelIncludes(prefs, key, k) && (!last || item.ts > last.ts)) last = item;
    return last;
  };

  const partners = partnersOf(items, character, []);
  const pinnedOnly = prefs.pinned.flatMap((key) => dmPartner(key, character) ?? []).filter((p) => !partners.includes(p));
  const rows = [
    ...[...partners, ...pinnedOnly].map((p) => dmRow(prefs, character, p, newest.get(`t|${character}|${p}`))),
    ...visibleGroups(prefs, character).map((c) => groupRow(prefs, c, lastOf(customKey(c)))),
  ];

  const pinned = prefs.pinned.flatMap((key) => rows.find((r) => r.key === key) ?? []);
  const rest = rows.filter((r) => !r.pinned);
  const recent = rest.filter((r) => r.last).sort((a, b) => b.last!.ts - a.last!.ts);
  return [...pinned, ...recent, ...rest.filter((r) => !r.last)];
}

export function previewText(row: ChatRow): string {
  const { last } = row;
  if (!last) return "";
  if (last.outgoing) return `You: ${last.text}`;
  if (row.kind === "dm" || !last.sender) return last.text;
  return `${last.sender.split(" ")[0]}: ${last.text}`;
}

// The chat a notification's message key opens: a tell's DM, or the first group showing that chat type.
export function chatForKey(rows: ChatRow[], prefs: ChannelPrefs, key: string): string | null {
  if (key.startsWith("t|")) return key;
  return rows.find((r) => channelIncludes(prefs, r.key, key))?.key ?? null;
}

// A DM with no messages yet still opens; a deleted group doesn't.
export function findChat(rows: ChatRow[], character: string, key: string | null, prefs: ChannelPrefs): ChatRow | null {
  if (!key) return null;
  const row = rows.find((r) => r.key === key);
  if (row) return row;
  const partner = dmPartner(key, character);
  if (!partner) return null;
  // A typed name can differ in case from the spelling the game sends its tells with.
  const lower = key.toLowerCase();
  return rows.find((r) => r.kind === "dm" && r.key.toLowerCase() === lower) ?? dmRow(prefs, character, partner);
}
