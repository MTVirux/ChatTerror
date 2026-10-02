import { isChatChannel, type ChannelNotifyPref } from "./protocol";

export type NotifyLevel = "all" | "none";
export type NotifyChoice = NotifyLevel | "default";

// Keyed by the UI channel key: "c|character|channel" or "t|character|partner".
export interface ChannelPrefs {
  pinned: string[];
  muted: string[];
  notify: Record<string, NotifyLevel>;
}

export const EMPTY_CHANNEL_PREFS: ChannelPrefs = { pinned: [], muted: [], notify: {} };

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

function notifyPref(key: string, notify: NotifyLevel): ChannelNotifyPref | null {
  const [kind, character, rest] = key.split("|");
  if (!character || !rest) return null;
  if (kind === "t") return { character, channel: "tell", partner: rest, notify };
  if (kind === "c" && isChatChannel(rest) && rest !== "tell") return { character, channel: rest, notify };
  return null;
}

// Mute wins over the notify choice; pins never leave the device.
export function channelNotifyPrefs(prefs: ChannelPrefs): ChannelNotifyPref[] {
  const keys = [...new Set([...prefs.muted, ...Object.keys(prefs.notify)])];
  return keys.flatMap((key) => notifyPref(key, prefs.muted.includes(key) ? "none" : prefs.notify[key]) ?? []);
}
