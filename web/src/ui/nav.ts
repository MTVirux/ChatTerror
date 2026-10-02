import type { AccountView } from "../core/accounts";
import { channelKey, firstChannel, type Category, type ChannelRef } from "./channels";

export type Server = "home" | "add" | string;

// channel is a channelKey, or null for the logged-in character's first channel.
export interface Nav {
  server: Server;
  channel: string | null;
}

export function validNav(nav: Nav, accounts: AccountView[]): Nav {
  if (accounts.length === 0) return { server: "add", channel: null };
  if (nav.server === "add") return { server: "add", channel: null };
  if (nav.server === "home") return accounts.length > 1 ? { server: "home", channel: null } : { server: accounts[0].deviceId, channel: null };
  if (!accounts.some((a) => a.deviceId === nav.server)) return { server: accounts[0].deviceId, channel: null };
  return { server: nav.server, channel: nav.channel };
}

function parseNav(stored: string | null): Nav | null {
  if (!stored) return null;
  try {
    const value = JSON.parse(stored);
    if (typeof value?.server !== "string") return null;
    return { server: value.server, channel: typeof value.channel === "string" ? value.channel : null };
  } catch {
    return null;
  }
}

// Last opened channel key per account.
export type LastChannels = Record<string, string>;

export function navTo(server: Server, last: LastChannels): Nav {
  return { server, channel: server === "home" || server === "add" ? null : last[server] ?? null };
}

export function rememberChannel(last: LastChannels, nav: Nav): LastChannels {
  if (!nav.channel || nav.server === "home" || nav.server === "add" || last[nav.server] === nav.channel) return last;
  return { ...last, [nav.server]: nav.channel };
}

export function parseLastChannels(stored: string | null): LastChannels {
  try {
    const value = JSON.parse(stored ?? "null");
    if (!value || typeof value !== "object" || Array.isArray(value)) return {};
    return Object.fromEntries(Object.entries(value).filter(([, key]) => typeof key === "string")) as LastChannels;
  } catch {
    return {};
  }
}

export function initialNav(accounts: AccountView[], hash: string, stored: string | null, last: LastChannels = {}): Nav {
  if (accounts.length > 0 && hash.startsWith("#pair=")) return { server: "add", channel: null };
  const fromLink = hash.match(/^#account=(.+)$/);
  if (fromLink) return validNav(navTo(decodeURIComponent(fromLink[1]), last), accounts);
  const saved = parseNav(stored);
  if (saved) return validNav(saved, accounts);
  return validNav({ server: accounts[0]?.deviceId ?? "add", channel: null }, accounts);
}

// The old UI stored "all" or a deviceId.
export function legacyNav(stored: string | null): string | null {
  if (!stored) return null;
  return serializeNav({ server: stored === "all" ? "home" : stored, channel: null });
}

export function serializeNav(nav: Nav): string {
  return JSON.stringify({ server: nav.server, channel: nav.channel });
}

export function resolveChannel(key: string | null, tree: Category[]): ChannelRef | null {
  for (const category of tree) {
    const ref = category.refs.find((r) => channelKey(r) === key);
    if (ref) return ref;
  }
  return firstChannel(tree);
}
