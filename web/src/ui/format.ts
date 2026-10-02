import { CHANNEL_LABELS, type ChatChannel, type ChatItem } from "../core/protocol";
import type { SessionStatus } from "../core/session";

export const TELL_TARGET = /^[A-Za-z'\-]{1,15} [A-Za-z'\-]{1,15}@[A-Za-z]{3,16}$/;

export function channelColor(channel: ChatChannel): string {
  if (channel.startsWith("crossLinkshell")) return "var(--ch-cwls)";
  if (channel.startsWith("linkshell")) return "var(--ch-ls)";
  return `var(--ch-${channel})`;
}

export function channelLabel(channel: ChatChannel): string {
  return CHANNEL_LABELS[channel] ?? channel;
}

// Outgoing tells carry the target as sender, so both directions map to the same partner.
export function tellPartner(item: ChatItem): string {
  if (!item.senderWorld || item.sender.includes("@")) return item.sender;
  return `${item.sender}@${item.senderWorld}`;
}

export function sendPrefix(channel: ChatChannel, target?: string): string {
  if (channel === "tell") return `/tell ${target ?? ""} `;
  if (channel.startsWith("crossLinkshell")) return `/cwl${channel.slice("crossLinkshell".length)} `;
  if (channel.startsWith("linkshell")) return `/l${channel.slice("linkshell".length)} `;
  const prefixes: Record<string, string> = {
    party: "/p ",
    alliance: "/a ",
    freeCompany: "/fc ",
    noviceNetwork: "/n ",
    say: "/s ",
    shout: "/sh ",
    yell: "/y ",
  };
  return prefixes[channel] ?? "";
}

const encoder = new TextEncoder();
export function byteLength(text: string): number {
  return encoder.encode(text).length;
}

const SEND_ERRORS: Record<string, string> = {
  channelNotAllowed: "Sending to this channel is disabled in the plugin",
  invalidText: "Message contains characters the game doesn't allow",
  tooLong: "Message is too long for the game",
  invalidTarget: "That player name or world isn't valid",
  notLoggedIn: "Character is not logged in",
  busy: "Game is busy (loading or cutscene)",
  disabled: "Sending from your phone is turned off in the plugin",
  timeout: "No response from the game",
  offline: "Not connected to the relay",
  gameOffline: "The game is offline",
  notFriend: "You are not on their friend list",
  notChatTerror: "They don't use ChatTerror",
  notOwner: "This character is registered to another ChatTerror install",
  keyChanged: "Their ChatTerror key changed. Forget them under Trusted ChatTerror friends in the plugin",
};

export function sendErrorText(code?: string): string {
  return (code && Object.hasOwn(SEND_ERRORS, code) && SEND_ERRORS[code]) || "Message failed to send";
}

export const STATUS_LABELS: Record<SessionStatus, string> = {
  unpaired: "Not paired",
  pending: "Waiting for approval",
  connecting: "Reconnecting",
  online: "Online",
  gameOffline: "Game offline",
  relayOffline: "Relay offline",
  revoked: "Removed",
};

export function sendBlockedReason(status: SessionStatus): string | null {
  if (status === "online") return null;
  if (status === "gameOffline") return "The game isn't running or the plugin is off";
  if (status === "relayOffline") return "Can't reach the relay. Check your connection";
  return "Connecting to the relay";
}

export function timeOfDay(ts: number): string {
  return new Date(ts).toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" });
}

export function dayLabel(ts: number): string {
  const d = new Date(ts);
  const today = new Date();
  const yesterday = new Date();
  yesterday.setDate(today.getDate() - 1);
  if (d.toDateString() === today.toDateString()) return "Today";
  if (d.toDateString() === yesterday.toDateString()) return "Yesterday";
  return d.toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" });
}

export function defaultDeviceName(): string {
  const ua = navigator.userAgent;
  if (/iPhone/.test(ua)) return "iPhone";
  if (/iPad/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) return "iPad";
  if (/Android/.test(ua)) return /Mobile/.test(ua) ? "Android phone" : "Android tablet";
  if (/Windows/.test(ua)) return "Windows";
  if (/Macintosh/.test(ua)) return "Mac";
  if (/CrOS/.test(ua)) return "Chromebook";
  if (/Linux/.test(ua)) return "Linux";
  return "Browser";
}

// The APK opens the site with this referrer; sessionStorage keeps the answer across reloads.
function isAndroidApp(): boolean {
  const fromApp = document.referrer.startsWith("android-app://app.mtvirux.chatterror");
  try {
    if (fromApp) sessionStorage.setItem("androidApp", "1");
    return sessionStorage.getItem("androidApp") === "1";
  } catch {
    return fromApp;
  }
}

export function offerAndroidApp(): boolean {
  return /Android/.test(navigator.userAgent) && !isAndroidApp();
}

export function isIos(): boolean {
  return /iPhone|iPad|iPod/.test(navigator.userAgent) || (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1);
}

export function needsHomeScreen(): boolean {
  return isIos() && (navigator as Navigator & { standalone?: boolean }).standalone === false;
}
