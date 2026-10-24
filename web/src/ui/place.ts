import type { AccountView } from "../core/accounts";
import { keyCharacter } from "./channels";

export const PLACE_KEY = "chatterror.place";
export const LAST_CHAT_KEY = "chatterror.lastChat";

// character is null while the client has none yet, or is pending or removed.
export interface Place {
  deviceId: string;
  character: string | null;
}

// The last open chat key per "deviceId|character".
export type Views = Record<string, string>;

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

// Notifications carry message keys, never custom ones.
export function chatTarget(chat: string): { character: string; chat: string } | null {
  const character = keyCharacter(chat);
  return character && !chat.startsWith("x|") ? { character, chat } : null;
}

export function initialPlace(accounts: AccountView[], hash: string, stored: string | null, charactersOf: (account: AccountView) => string[]): Place | null {
  const link = parseLink(hash);
  if (link && accounts.some((a) => a.deviceId === link.deviceId)) {
    const character = link.chat ? keyCharacter(link.chat) ?? null : null;
    return validPlace({ deviceId: link.deviceId, character }, accounts, charactersOf);
  }
  return validPlace(parsePlace(stored), accounts, charactersOf);
}
