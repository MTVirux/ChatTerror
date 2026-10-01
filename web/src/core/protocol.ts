type LinkshellNumber = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;

export type ChatChannel =
  | "tell"
  | "party"
  | "alliance"
  | "freeCompany"
  | `linkshell${LinkshellNumber}`
  | `crossLinkshell${LinkshellNumber}`
  | "noviceNetwork"
  | "say"
  | "shout"
  | "yell";

const LINKSHELLS = [1, 2, 3, 4, 5, 6, 7, 8] as const;

export const ALL_CHANNELS: ChatChannel[] = [
  "tell",
  "party",
  "alliance",
  "freeCompany",
  ...LINKSHELLS.map((n) => `linkshell${n}` as const),
  ...LINKSHELLS.map((n) => `crossLinkshell${n}` as const),
  "noviceNetwork",
  "say",
  "shout",
  "yell",
];

export const CHANNEL_LABELS = {
  tell: "Tell",
  party: "Party",
  alliance: "Alliance",
  freeCompany: "FC",
  ...Object.fromEntries(LINKSHELLS.map((n) => [`linkshell${n}`, `LS${n}`])),
  ...Object.fromEntries(LINKSHELLS.map((n) => [`crossLinkshell${n}`, `CWLS${n}`])),
  noviceNetwork: "Novice",
  say: "Say",
  shout: "Shout",
  yell: "Yell",
} as Record<ChatChannel, string>;

export function isChatChannel(value: unknown): value is ChatChannel {
  return typeof value === "string" && (ALL_CHANNELS as string[]).includes(value);
}

export interface ChatItem {
  id: string;
  ts: number;
  channel: ChatChannel;
  sender: string;
  senderWorld?: string;
  text: string;
  character: string;
  outgoing: boolean;
}

export type SendError = "channelNotAllowed" | "invalidText" | "tooLong" | "invalidTarget" | "notLoggedIn" | "busy" | "disabled";

export interface ChatPayload { type: "chat"; seq: number; item: ChatItem }
export interface BacklogPayload { type: "backlog"; seq: number; items: ChatItem[]; done: boolean }
export interface SendResultPayload { type: "sendResult"; seq: number; requestId: string; ok: boolean; error?: SendError }
export interface SettingsPayload {
  type: "settings";
  seq: number;
  character?: string;
  relayChannels: ChatChannel[];
  sendChannels: ChatChannel[];
  maxLength: number;
}

export interface HelloPayload { type: "hello"; seq: number; sinceTs: number }
export interface SendChatPayload { type: "sendChat"; seq: number; requestId: string; channel: ChatChannel; target?: string; text: string }
export interface PrefsPayload { type: "prefs"; seq: number; mutedChannels: ChatChannel[] }

export type PluginPayload = ChatPayload | BacklogPayload | SendResultPayload | SettingsPayload;
export type DevicePayload = HelloPayload | SendChatPayload | PrefsPayload;
export type Payload = PluginPayload | DevicePayload;

export interface AuthFrame { t: "auth"; token: string }
export interface SendFrame { t: "send"; to?: string; payload: string; notify?: boolean }
export interface PairDecisionFrame { t: "pairDecision"; deviceId: string; approved: boolean }
export interface AuthOkFrame { t: "authOk"; role: "plugin" | "device"; id: string }
export interface AuthFailFrame { t: "authFail" }
export interface MsgFrame { t: "msg"; from: string; payload: string }
export interface DeviceOnlineFrame { t: "deviceOnline"; deviceId: string }
export interface DeviceOfflineFrame { t: "deviceOffline"; deviceId: string }
export interface PairRequestFrame { t: "pairRequest"; deviceId: string; deviceName: string; devicePublicKey: string }
export interface DeviceRevokedFrame { t: "deviceRevoked"; deviceId: string }
export interface PluginStatusFrame { t: "pluginStatus"; online: boolean }
export interface PairedFrame { t: "paired" }
export interface RevokedFrame { t: "revoked" }
export interface ErrorFrame { t: "error"; code: "rateLimited" | "tooLarge" | "unknownDevice" | "notApproved" | "badFrame" }

// What a device sends and receives; plugin-only frames are listed for completeness.
export type ClientFrame = AuthFrame | SendFrame;
export type ServerFrame = AuthOkFrame | AuthFailFrame | MsgFrame | PluginStatusFrame | PairedFrame | RevokedFrame | ErrorFrame;
export type RelayFrame =
  | ClientFrame
  | ServerFrame
  | PairDecisionFrame
  | DeviceOnlineFrame
  | DeviceOfflineFrame
  | PairRequestFrame
  | DeviceRevokedFrame;

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export function isChatItem(value: unknown): value is ChatItem {
  return (
    isObject(value) &&
    typeof value.id === "string" &&
    typeof value.ts === "number" &&
    isChatChannel(value.channel) &&
    typeof value.sender === "string" &&
    (value.senderWorld === undefined || typeof value.senderWorld === "string") &&
    typeof value.text === "string" &&
    typeof value.character === "string" &&
    typeof value.outgoing === "boolean"
  );
}

function isChannelList(value: unknown): value is ChatChannel[] {
  return Array.isArray(value) && value.every(isChatChannel);
}

export function parsePluginPayload(value: unknown): PluginPayload | null {
  if (!isObject(value) || typeof value.seq !== "number") return null;
  switch (value.type) {
    case "chat":
      return isChatItem(value.item) ? (value as unknown as ChatPayload) : null;
    case "backlog":
      return Array.isArray(value.items) && value.items.every(isChatItem) && typeof value.done === "boolean"
        ? (value as unknown as BacklogPayload)
        : null;
    case "sendResult":
      return typeof value.requestId === "string" && typeof value.ok === "boolean"
        ? (value as unknown as SendResultPayload)
        : null;
    case "settings":
      return isChannelList(value.relayChannels) && isChannelList(value.sendChannels) && typeof value.maxLength === "number"
        ? (value as unknown as SettingsPayload)
        : null;
    default:
      return null;
  }
}

const SERVER_FRAME_TYPES = ["authOk", "authFail", "msg", "pluginStatus", "paired", "revoked", "error"];

export function parseServerFrame(text: string): ServerFrame | null {
  try {
    const value: unknown = JSON.parse(text);
    if (!isObject(value) || !SERVER_FRAME_TYPES.includes(value.t as string)) return null;
    if (value.t === "msg" && typeof value.payload !== "string") return null;
    if (value.t === "pluginStatus" && typeof value.online !== "boolean") return null;
    return value as unknown as ServerFrame;
  } catch {
    return null;
  }
}
