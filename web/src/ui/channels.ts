import { channelIncludes, customKey, savedOrder, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import type { ChatChannel, ChatItem } from "../core/protocol";
import { channelLabel, tellPartner } from "./format";

export type ChannelRef =
  | { kind: "custom"; character: string; id: string }
  | { kind: "tell"; character: string; partner: string };

export interface Category {
  character: string;
  active: boolean;
  // Rows in display order, custom channels before tells unless reordered.
  refs: ChannelRef[];
  lastTs: number;
}

export function channelKey(ref: ChannelRef): string {
  return ref.kind === "custom" ? customKey(ref) : `t|${ref.character}|${ref.partner}`;
}

export function parseChannelKey(key: string): ChannelRef | null {
  const [kind, character, rest] = key.split("|");
  if (!character || !rest) return null;
  if (kind === "x") return { kind: "custom", character, id: rest };
  if (kind === "t") return { kind: "tell", character, partner: rest };
  return null;
}

// Messages are keyed by their in-game channel: "c|character|channel" or "t|character|partner".
export function itemKey(item: ChatItem): string {
  return item.channel === "tell" ? `t|${item.character}|${tellPartner(item)}` : `c|${item.character}|${item.channel}`;
}

export function inChannel(item: ChatItem, ref: ChannelRef, prefs: ChannelPrefs): boolean {
  return channelIncludes(prefs, channelKey(ref), itemKey(item));
}

export function findCustom(prefs: ChannelPrefs, ref: ChannelRef): CustomChannel | undefined {
  return ref.kind === "custom" ? prefs.custom.find((c) => c.character === ref.character && c.id === ref.id) : undefined;
}

export function channelSlug(channel: ChatChannel): string {
  return channelLabel(channel).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

// Saved order first, then rows it doesn't know yet in their default place; pins lead.
export function arrange(refs: ChannelRef[], order: string[] = [], pinned: string[] = []): ChannelRef[] {
  const byKey = new Map(refs.map((r) => [channelKey(r), r]));
  const ordered = order.flatMap((key) => byKey.get(key) ?? []);
  return pinFirst([...ordered, ...refs.filter((r) => !order.includes(channelKey(r)))], pinned);
}

export function buildChannelTree(items: ChatItem[], { character, prefs }: { character?: string; prefs: ChannelPrefs }): Category[] {
  const seen = new Map<string, { tells: Map<string, number>; lastTs: number }>();
  const entry = (name: string) => {
    let e = seen.get(name);
    if (!e) seen.set(name, (e = { tells: new Map(), lastTs: 0 }));
    return e;
  };
  if (character) entry(character);
  for (const c of prefs.custom) entry(c.character);
  for (const item of items) {
    const e = entry(item.character);
    e.lastTs = Math.max(e.lastTs, item.ts);
    if (item.channel === "tell") {
      const partner = tellPartner(item);
      e.tells.set(partner, Math.max(e.tells.get(partner) ?? 0, item.ts));
    }
  }

  const categories = [...seen.entries()].map(([name, e]): Category => {
    const customs: ChannelRef[] = prefs.custom.filter((c) => c.character === name).map((c) => ({ kind: "custom", character: name, id: c.id }));
    const tells: ChannelRef[] = [...e.tells.entries()].sort((a, b) => b[1] - a[1]).map(([partner]) => ({ kind: "tell", character: name, partner }));
    return { character: name, active: name === character, refs: arrange([...customs, ...tells], savedOrder(prefs, name), prefs.pinned), lastTs: e.lastTs };
  });
  return categories.sort((a, b) => Number(b.active) - Number(a.active) || b.lastTs - a.lastTs);
}

export function firstChannel(categories: Category[]): ChannelRef | null {
  return categories[0]?.refs[0] ?? null;
}

// Characters listed in toggled are flipped from the default, where only the logged-in one is expanded.
export function isCollapsed(category: Pick<Category, "character" | "active">, toggled: string[]): boolean {
  return category.active === toggled.includes(category.character);
}

export function toggleCategory(toggled: string[], character: string): string[] {
  return toggled.includes(character) ? toggled.filter((c) => c !== character) : [...toggled, character];
}

// Pinned rows lead in the order they were pinned, the rest keep their place.
export function pinFirst(refs: ChannelRef[], pinned: string[]): ChannelRef[] {
  const byKey = new Map(refs.map((r) => [channelKey(r), r]));
  const top = pinned.flatMap((key) => byKey.get(key) ?? []);
  return [...top, ...refs.filter((r) => !pinned.includes(channelKey(r)))];
}
