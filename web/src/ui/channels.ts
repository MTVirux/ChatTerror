import type { AccountView } from "../core/accounts";
import { channelIncludes, customKey, savedOrder, TELLS_ID, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import type { ChatChannel, ChatItem } from "../core/protocol";
import { channelColor, channelLabel, tellPartner } from "./format";

// One row in a channel's list. key is what the row shows: the custom key for "all", "c|character|channel" or "t|character|partner".
export interface SubRow {
  key: string;
  kind: "all" | "type" | "partner";
  label: string;
  // Shown as "# label"; partner rows and "All tells" are not.
  hash: boolean;
  channel?: ChatChannel;
  partner?: string;
}

// Messages are keyed by their in-game channel: "c|character|channel" or "t|character|partner".
export function itemKey(item: ChatItem): string {
  return item.channel === "tell" ? `t|${item.character}|${tellPartner(item)}` : `c|${item.character}|${item.channel}`;
}

export function keyCharacter(key: string): string | undefined {
  const [kind, character, rest] = key.split("|");
  return (kind === "c" || kind === "t" || kind === "x") && character && rest ? character : undefined;
}

export function channelSlug(channel: ChatChannel): string {
  return channelLabel(channel).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

export function isTells(custom: CustomChannel): boolean {
  return custom.id === TELLS_ID;
}

export function customColor(custom: CustomChannel | undefined): string {
  const first = custom?.channels.find((c) => c !== "tell") ?? custom?.channels[0];
  return first ? channelColor(first) : "var(--muted)";
}

// Tells first, then the character's other channels in saved order, unknown ones after in creation order.
export function railChannels(prefs: ChannelPrefs, character: string): CustomChannel[] {
  const own = prefs.custom.filter((c) => c.character === character);
  const rest = own.filter((c) => !isTells(c));
  const saved = (savedOrder(prefs, character) ?? []).flatMap((key) => rest.find((c) => customKey(c) === key) ?? []);
  return [...own.filter(isTells), ...saved, ...rest.filter((c) => !saved.includes(c))];
}

export function channelFor(prefs: ChannelPrefs, character: string, key: string): CustomChannel | undefined {
  return railChannels(prefs, character).find((c) => channelIncludes(prefs, customKey(c), key));
}

// Pinned partners lead in pin order, the rest newest first.
export function partnersOf(items: ChatItem[], character: string, pinned: string[]): string[] {
  const latest = new Map<string, number>();
  for (const item of items) {
    if (item.channel !== "tell" || item.character !== character) continue;
    const partner = tellPartner(item);
    latest.set(partner, Math.max(latest.get(partner) ?? 0, item.ts));
  }
  const newest = [...latest].sort((a, b) => b[1] - a[1]).map(([partner]) => partner);
  const top = pinned.flatMap((key) => newest.filter((p) => `t|${character}|${p}` === key));
  return [...top, ...newest.filter((p) => !top.includes(p))];
}

// A channel lists "all" first, then each chat type when there is more than one thing to pick, then its tell partners.
export function subRows(custom: CustomChannel, partners: string[]): SubRow[] {
  const { character } = custom;
  const types = custom.channels.filter((c) => c !== "tell");
  const hasTell = custom.channels.includes("tell");
  const allLabel = types.length === 0 ? "All tells" : types.length === 1 && !hasTell ? channelSlug(types[0]) : "all";
  const rows: SubRow[] = [{ key: customKey(custom), kind: "all", label: allLabel, hash: types.length > 0 }];
  if (types.length + (hasTell ? 1 : 0) > 1) {
    for (const channel of types) rows.push({ key: `c|${character}|${channel}`, kind: "type", label: channelSlug(channel), hash: true, channel });
  }
  if (hasTell) {
    for (const partner of partners) rows.push({ key: `t|${character}|${partner}`, kind: "partner", label: partner.split("@")[0], hash: false, partner });
  }
  return rows;
}

// One chat leaves nothing to pick, so the drawer shows only the rail.
export function hasOneChat(custom: CustomChannel | null): boolean {
  return !!custom && custom.channels.length === 1 && custom.channels[0] !== "tell";
}

// Only a channel's "all" view mixes chats, so only there does each message need its chat named.
export function showsChatTags(custom: CustomChannel | null, row: SubRow | null): boolean {
  return !!custom && row?.kind === "all" && custom.channels.length > 1;
}

// The logged-in character first, then the rest by name. items adds characters only seen in that client's messages.
export function charactersOf(account: AccountView, items: ChatItem[] = []): string[] {
  const { seeded, custom } = account.state.channelPrefs;
  const names = new Set([...seeded, ...custom.map((c) => c.character), ...items.map((i) => i.character)]);
  const current = account.character;
  if (current) names.delete(current);
  const others = [...names].filter(Boolean).sort((a, b) => a.localeCompare(b));
  return current ? [current, ...others] : others;
}
