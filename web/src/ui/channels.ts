import { ALL_CHANNELS, type ChatChannel, type ChatItem } from "../core/protocol";
import { channelLabel, tellPartner } from "./format";

export type ChannelRef =
  | { kind: "chat"; character: string; channel: ChatChannel }
  | { kind: "tell"; character: string; partner: string };

export interface TellEntry {
  partner: string;
  ts: number;
}

export interface Category {
  character: string;
  active: boolean;
  chats: ChatChannel[];
  tells: TellEntry[];
  lastTs: number;
}

export function channelKey(ref: ChannelRef): string {
  return ref.kind === "chat" ? `c|${ref.character}|${ref.channel}` : `t|${ref.character}|${ref.partner}`;
}

export function parseChannelKey(key: string): ChannelRef | null {
  const [kind, character, rest] = key.split("|");
  if (!character || !rest) return null;
  if (kind === "c" && (ALL_CHANNELS as string[]).includes(rest)) return { kind: "chat", character, channel: rest as ChatChannel };
  if (kind === "t") return { kind: "tell", character, partner: rest };
  return null;
}

export function itemChannelRef(item: ChatItem): ChannelRef {
  return item.channel === "tell"
    ? { kind: "tell", character: item.character, partner: tellPartner(item) }
    : { kind: "chat", character: item.character, channel: item.channel };
}

export function inChannel(item: ChatItem, ref: ChannelRef): boolean {
  return channelKey(itemChannelRef(item)) === channelKey(ref);
}

export function channelSlug(channel: ChatChannel): string {
  return channelLabel(channel).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

// Plugin order first, then everything else in protocol order.
function ordered(channels: Set<ChatChannel>, relay: ChatChannel[]): ChatChannel[] {
  const order = [...relay, ...ALL_CHANNELS.filter((c) => !relay.includes(c))];
  return order.filter((c) => channels.has(c));
}

export function buildChannelTree(items: ChatItem[], active: { character?: string; relayChannels: ChatChannel[]; showEmpty?: boolean }): Category[] {
  const seen = new Map<string, { chats: Set<ChatChannel>; tells: Map<string, number>; lastTs: number }>();
  const entry = (character: string) => {
    let e = seen.get(character);
    if (!e) seen.set(character, (e = { chats: new Set(), tells: new Map(), lastTs: 0 }));
    return e;
  };
  if (active.character) entry(active.character);
  for (const item of items) {
    const e = entry(item.character);
    e.lastTs = Math.max(e.lastTs, item.ts);
    if (item.channel === "tell") {
      const partner = tellPartner(item);
      e.tells.set(partner, Math.max(e.tells.get(partner) ?? 0, item.ts));
    } else {
      e.chats.add(item.channel);
    }
  }

  const categories = [...seen.entries()].map(([character, e]): Category => {
    const isActive = character === active.character;
    if (isActive && active.showEmpty) for (const c of active.relayChannels) if (c !== "tell") e.chats.add(c);
    return {
      character,
      active: isActive,
      chats: ordered(e.chats, active.relayChannels),
      tells: [...e.tells.entries()].map(([partner, ts]) => ({ partner, ts })).sort((a, b) => b.ts - a.ts),
      lastTs: e.lastTs,
    };
  });
  return categories.sort((a, b) => Number(b.active) - Number(a.active) || b.lastTs - a.lastTs);
}

export function firstChannel(categories: Category[]): ChannelRef | null {
  const first = categories[0];
  if (!first) return null;
  if (first.chats.length > 0) return { kind: "chat", character: first.character, channel: first.chats[0] };
  if (first.tells.length > 0) return { kind: "tell", character: first.character, partner: first.tells[0].partner };
  return null;
}

// Characters listed in toggled are flipped from the default, where only the logged-in one is expanded.
export function isCollapsed(category: Pick<Category, "character" | "active">, toggled: string[]): boolean {
  return category.active === toggled.includes(category.character);
}

export function toggleCategory(toggled: string[], character: string): string[] {
  return toggled.includes(character) ? toggled.filter((c) => c !== character) : [...toggled, character];
}
