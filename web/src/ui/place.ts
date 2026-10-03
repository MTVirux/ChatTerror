import type { AccountView } from "../core/accounts";
import { customKey, withTells, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import { channelFor, keyCharacter, type SubRow } from "./channels";

export const PLACE_KEY = "chatterror.place";
export const SERVER_KEY = "chatterror.lastServer";
export const SUB_KEY = "chatterror.lastSub";

// character is null while the client has none yet, or is pending or removed.
export interface Place {
  deviceId: string;
  character: string | null;
}

// Remembered picks: channel id per "deviceId|character", row key per "deviceId|custom key".
export type Views = Record<string, string>;

export interface Picks {
  server: Views;
  sub: Views;
}

export function viewKey(deviceId: string, key: string): string {
  return `${deviceId}|${key}`;
}

export function parsePlace(stored: string | null): Place | null {
  try {
    const value = JSON.parse(stored ?? "null");
    if (typeof value?.deviceId !== "string") return null;
    return { deviceId: value.deviceId, character: typeof value.character === "string" ? value.character : null };
  } catch {
    return null;
  }
}

export function parseViews(stored: string | null): Views {
  try {
    const value = JSON.parse(stored ?? "null");
    if (!value || typeof value !== "object" || Array.isArray(value)) return {};
    return Object.fromEntries(Object.entries(value).filter(([, v]) => typeof v === "string")) as Views;
  } catch {
    return {};
  }
}

export function remember(views: Views, key: string, value: string): Views {
  return Object.hasOwn(views, key) && views[key] === value ? views : { ...views, [key]: value };
}

function usable(account: AccountView): boolean {
  return account.status === "active" && account.state.status !== "pending";
}

// A character not in charactersOf is kept, since its messages may just not be loaded yet.
export function validPlace(place: Place | null, accounts: AccountView[], charactersOf: (account: AccountView) => string[]): Place | null {
  if (accounts.length === 0) return null;
  const account = (place && accounts.find((a) => a.deviceId === place.deviceId)) ?? accounts.find(usable) ?? accounts[0];
  if (!usable(account)) return { deviceId: account.deviceId, character: null };
  const character = place?.deviceId === account.deviceId && place.character ? place.character : charactersOf(account)[0] ?? null;
  return { deviceId: account.deviceId, character };
}

// "#account=<deviceId>" from a notification, with "&chat=<item key>" for the message it was about.
export interface Link {
  deviceId: string;
  chat: string | null;
}

export function parseLink(hash: string): Link | null {
  const match = hash.match(/^#account=([^&]+)(?:&chat=(.+))?$/);
  if (!match) return null;
  try {
    return { deviceId: decodeURIComponent(match[1]), chat: match[2] ? decodeURIComponent(match[2]) : null };
  } catch {
    return null;
  }
}

export function accountLink(deviceId: string, chat?: string): string {
  return `/#account=${encodeURIComponent(deviceId)}${chat ? `&chat=${encodeURIComponent(chat)}` : ""}`;
}

export interface ChatTarget {
  character: string;
  // Unset when no channel shows this chat type; the character still opens.
  channelId?: string;
  rowKey: string;
}

export function chatTarget(prefs: ChannelPrefs, chat: string): ChatTarget | null {
  const character = keyCharacter(chat);
  if (!character || chat.startsWith("x|")) return null;
  return { character, channelId: channelFor(withTells(prefs, character), character, chat)?.id, rowKey: chat };
}

export function pickTarget(picks: Picks, deviceId: string, target: ChatTarget): Picks {
  if (!target.channelId) return picks;
  const custom = customKey({ character: target.character, id: target.channelId });
  return {
    server: remember(picks.server, viewKey(deviceId, target.character), target.channelId),
    sub: remember(picks.sub, viewKey(deviceId, custom), target.rowKey),
  };
}

export function initialPlace(accounts: AccountView[], hash: string, stored: string | null, charactersOf: (account: AccountView) => string[]): Place | null {
  const link = parseLink(hash);
  if (link && accounts.some((a) => a.deviceId === link.deviceId)) {
    const character = link.chat ? keyCharacter(link.chat) ?? null : null;
    return validPlace({ deviceId: link.deviceId, character }, accounts, charactersOf);
  }
  return validPlace(parsePlace(stored), accounts, charactersOf);
}

export function resolveChannel(rail: CustomChannel[], id: string | undefined): CustomChannel | null {
  return rail.find((c) => c.id === id) ?? rail[0] ?? null;
}

export function resolveRow(rows: SubRow[], key: string | undefined): SubRow {
  return rows.find((r) => r.key === key) ?? rows[0];
}
