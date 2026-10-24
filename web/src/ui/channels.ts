import type { AccountView } from "../core/accounts";
import type { CustomChannel } from "../core/channelPrefs";
import type { ChatChannel, ChatItem } from "../core/protocol";
import { channelColor, channelLabel, tellPartner } from "./format";

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

export function customColor(custom: CustomChannel | undefined): string {
  const first = custom?.channels.find((c) => c !== "tell") ?? custom?.channels[0];
  return first ? channelColor(first) : "var(--muted)";
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

// Only a group mixing chats needs each message's chat named.
export function showsChatTags(custom: CustomChannel | undefined): boolean {
  return !!custom && custom.channels.length > 1;
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
