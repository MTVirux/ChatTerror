import type { AccountView } from "../core/accounts";
import { firstChannel, parseChannelKey, type Category, type ChannelRef } from "./channels";

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

export function initialNav(accounts: AccountView[], hash: string, stored: string | null): Nav {
  if (accounts.length > 0 && hash.startsWith("#pair=")) return { server: "add", channel: null };
  const fromLink = hash.match(/^#account=(.+)$/);
  if (fromLink) return validNav({ server: decodeURIComponent(fromLink[1]), channel: null }, accounts);
  const saved = parseNav(stored);
  if (saved) return validNav(saved, accounts);
  return validNav({ server: accounts[0]?.deviceId ?? "add", channel: null }, accounts);
}

export function serializeNav(nav: Nav): string {
  return JSON.stringify({ server: nav.server, channel: nav.channel });
}

export function resolveChannel(key: string | null, tree: Category[]): ChannelRef | null {
  const ref = key ? parseChannelKey(key) : null;
  const category = ref && tree.find((c) => c.character === ref.character);
  if (ref && category) {
    const exists = ref.kind === "chat" ? category.chats.includes(ref.channel) : category.tells.some((t) => t.partner === ref.partner);
    if (exists) return ref;
  }
  return firstChannel(tree);
}
