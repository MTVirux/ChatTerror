import { isChatChannel, type ChannelNotifyPref, type ChatChannel } from "./protocol";

export type NotifyLevel = "all" | "none";
export type NotifyChoice = NotifyLevel | "default";

// "tell" in channels stands for every tell of that character.
export interface CustomChannel {
  id: string;
  character: string;
  name: string;
  channels: ChatChannel[];
}

// Keyed by the UI channel key: "x|character|id" for custom channels or "t|character|partner" for tells.
export interface ChannelPrefs {
  pinned: string[];
  muted: string[];
  notify: Record<string, NotifyLevel>;
  custom: CustomChannel[];
  // Row order per character, as channel keys.
  order: Record<string, string[]>;
}

export const EMPTY_CHANNEL_PREFS: ChannelPrefs = { pinned: [], muted: [], notify: {}, custom: [], order: {} };

// Prefs from before custom channels lack the newer fields and may hold "c|" keys for rows that no longer exist.
export function withDefaults(prefs: Partial<ChannelPrefs> | undefined): ChannelPrefs {
  const merged = { ...EMPTY_CHANNEL_PREFS, ...prefs };
  const kept = (key: string) => !key.startsWith("c|");
  return {
    ...merged,
    pinned: merged.pinned.filter(kept),
    muted: merged.muted.filter(kept),
    notify: Object.fromEntries(Object.entries(merged.notify).filter(([key]) => kept(key))),
  };
}

export function customKey(custom: Pick<CustomChannel, "character" | "id">): string {
  return `x|${custom.character}|${custom.id}`;
}

function toggle(list: string[], key: string): string[] {
  return list.includes(key) ? list.filter((k) => k !== key) : [...list, key];
}

export function togglePinned(prefs: ChannelPrefs, key: string): ChannelPrefs {
  return { ...prefs, pinned: toggle(prefs.pinned, key) };
}

export function toggleMuted(prefs: ChannelPrefs, key: string): ChannelPrefs {
  return { ...prefs, muted: toggle(prefs.muted, key) };
}

export function notifyChoice(prefs: ChannelPrefs, key: string): NotifyChoice {
  return prefs.notify[key] ?? "default";
}

export function setNotify(prefs: ChannelPrefs, key: string, choice: NotifyChoice): ChannelPrefs {
  const { [key]: _, ...notify } = prefs.notify;
  return { ...prefs, notify: choice === "default" ? notify : { ...notify, [key]: choice } };
}

export function saveCustom(prefs: ChannelPrefs, custom: CustomChannel): ChannelPrefs {
  const exists = prefs.custom.some((c) => c.id === custom.id);
  return { ...prefs, custom: exists ? prefs.custom.map((c) => (c.id === custom.id ? custom : c)) : [...prefs.custom, custom] };
}

export function deleteCustom(prefs: ChannelPrefs, custom: CustomChannel): ChannelPrefs {
  const key = customKey(custom);
  const { [key]: _, ...notify } = prefs.notify;
  const order = prefs.order[custom.character];
  return {
    pinned: prefs.pinned.filter((k) => k !== key),
    muted: prefs.muted.filter((k) => k !== key),
    notify,
    custom: prefs.custom.filter((c) => c.id !== custom.id),
    order: order ? { ...prefs.order, [custom.character]: order.filter((k) => k !== key) } : prefs.order,
  };
}

// keys is the order the rows are shown in; the result keeps that order with key swapped by one step.
export function moveChannel(prefs: ChannelPrefs, character: string, keys: string[], key: string, step: -1 | 1): ChannelPrefs {
  const from = keys.indexOf(key);
  const to = from + step;
  if (from < 0 || to < 0 || to >= keys.length) return prefs;
  const next = [...keys];
  [next[from], next[to]] = [next[to], next[from]];
  return { ...prefs, order: { ...prefs.order, [character]: next } };
}

function isMember(custom: CustomChannel, key: string): boolean {
  const [kind, character, rest] = key.split("|");
  if (character !== custom.character || !rest) return false;
  if (kind === "t") return custom.channels.includes("tell");
  return kind === "c" && custom.channels.includes(rest as ChatChannel);
}

// Whether a message key ("c|character|channel" or "t|character|partner") belongs to the channel shown under key.
export function channelIncludes(prefs: ChannelPrefs, key: string, itemKey: string): boolean {
  if (!key.startsWith("x|")) return key === itemKey;
  const custom = prefs.custom.find((c) => customKey(c) === key);
  return !!custom && isMember(custom, itemKey);
}

export function isItemMuted(prefs: ChannelPrefs, itemKey: string): boolean {
  if (prefs.muted.includes(itemKey)) return true;
  return prefs.custom.some((c) => prefs.muted.includes(customKey(c)) && isMember(c, itemKey));
}

function notifyPref(key: string, notify: NotifyLevel): ChannelNotifyPref | null {
  const [kind, character, rest] = key.split("|");
  if (!character || !rest) return null;
  if (kind === "t") return { character, channel: "tell", partner: rest, notify };
  if (kind === "c" && isChatChannel(rest) && rest !== "tell") return { character, channel: rest, notify };
  return null;
}

// A custom channel's choice applies to its chat channels; the plugin only matches tells per partner.
function expand(prefs: ChannelPrefs, key: string): string[] {
  if (!key.startsWith("x|")) return [key];
  const custom = prefs.custom.find((c) => customKey(c) === key);
  return custom ? custom.channels.filter((c) => c !== "tell").map((c) => `c|${custom.character}|${c}`) : [];
}

// Mute wins over the notify choice; pins never leave the device.
export function channelNotifyPrefs(prefs: ChannelPrefs): ChannelNotifyPref[] {
  const levels = new Map<string, NotifyLevel>();
  for (const [key, level] of Object.entries(prefs.notify)) for (const k of expand(prefs, key)) levels.set(k, level);
  for (const key of prefs.muted) for (const k of expand(prefs, key)) levels.set(k, "none");
  return [...levels].flatMap(([key, level]) => notifyPref(key, level) ?? []);
}
