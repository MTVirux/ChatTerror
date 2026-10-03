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
  | "yell"
  | "echo"
  | "emote"
  | "pvpTeam"
  | "system"
  | "error"
  | "sales"
  | "loot"
  | "progress"
  | "crafting"
  | "gathering"
  | "npcDialogue"
  | "announcements"
  | "randomNumber"
  | "battle"
  | "gm";

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
  "echo",
  "emote",
  "pvpTeam",
  "system",
  "error",
  "sales",
  "loot",
  "progress",
  "crafting",
  "gathering",
  "npcDialogue",
  "announcements",
  "randomNumber",
  "battle",
  "gm",
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
  echo: "Echo",
  emote: "Emote",
  pvpTeam: "PvP Team",
  system: "System",
  error: "Error",
  sales: "Sales",
  loot: "Loot",
  progress: "Progress",
  crafting: "Crafting",
  gathering: "Gathering",
  npcDialogue: "NPC",
  announcements: "Announce",
  randomNumber: "Random",
  battle: "Battle",
  gm: "GM",
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
  contacts?: TellContact[];
}

export interface HelloPayload { type: "hello"; seq: number; sinceTs: number }
export interface SendChatPayload { type: "sendChat"; seq: number; requestId: string; channel: ChatChannel; target?: string; text: string }
export interface ChannelNotifyPref { character: string; channel: ChatChannel; partner?: string; notify: "all" | "none" }
export interface PrefsPayload { type: "prefs"; seq: number; mutedChannels: ChatChannel[]; channels: ChannelNotifyPref[] }

export type PluginPayload = ChatPayload | BacklogPayload | SendResultPayload | SettingsPayload;
export interface TellBody {
  id: string;
  fromHash: string;
  fromName: string;
  fromWorld: string;
  toHash: string;
  toName: string;
  toWorld: string;
  text: string;
  ts: number;
}
export interface TellBundleEntry { target: string; key: string; push: boolean }
export interface TellBundle { installPublicKey: string; entries: TellBundleEntry[]; issuedAt: number }
export interface SignedBundle { bundle: string; signature: string }
// A routable character of a paired friend. key is that friend's paired install key.
export interface TellContact { character: string; characterWorld: string; characterHash: string; name: string; world: string; hash: string; installId: string; key: string }
export interface TellCopy { self: boolean; target: string; envelope: string }
export interface TellKeyPayload { type: "tellKey"; seq: number; publicKey: string }

export type DevicePayload = HelloPayload | SendChatPayload | PrefsPayload | TellKeyPayload;
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
// to is the recipient install id.
export interface TellSendFrame { t: "tellSend"; id: string; to: string; copies: TellCopy[] }
export interface TellAckFrame { t: "tellAck"; ids: string[] }
// from is the sending install id.
export interface TellFrame { t: "tell"; id: string; from: string; envelope: string; fromKey: string }
export interface TellResultFrame { t: "tellResult"; id: string; ok: boolean; error?: string }

export type ClientFrame = AuthFrame | SendFrame | TellSendFrame | TellAckFrame;
export type ServerFrame = AuthOkFrame | AuthFailFrame | MsgFrame | PluginStatusFrame | PairedFrame | RevokedFrame | ErrorFrame | TellFrame | TellResultFrame;
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

const DAY_MS = 24 * 60 * 60 * 1000;
const MAX_TEXT_LENGTH = 4096;
const MAX_NAME_LENGTH = 64;
// A tell hash is base64url SHA-256, an install key base64url raw P-256.
const MAX_HASH_LENGTH = 43;
const MAX_KEY_LENGTH = 87;
// Matches Limits.MaxTextBytes in the plugin.
const MAX_SEND_LENGTH = 500;

function isBoundedString(value: unknown, max: number): value is string {
  return typeof value === "string" && value.length <= max;
}

// Names end up inside "kind|character|rest" channel keys.
function isName(value: unknown): value is string {
  return isBoundedString(value, MAX_NAME_LENGTH) && !value.includes("|");
}

export function isValidTs(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= Date.now() + DAY_MS;
}

export function isChatItem(value: unknown): value is ChatItem {
  return (
    isObject(value) &&
    isBoundedString(value.id, MAX_NAME_LENGTH) &&
    isValidTs(value.ts) &&
    isChatChannel(value.channel) &&
    isName(value.sender) &&
    (value.senderWorld === undefined || isName(value.senderWorld)) &&
    isBoundedString(value.text, MAX_TEXT_LENGTH) &&
    isName(value.character) &&
    typeof value.outgoing === "boolean"
  );
}

function isChannelList(value: unknown): value is ChatChannel[] {
  return Array.isArray(value) && value.every(isChatChannel);
}

function isSettings(value: unknown): value is Record<string, unknown> {
  return (
    isObject(value) &&
    (value.character === undefined || value.character === null || isName(value.character)) &&
    isChannelList(value.relayChannels) &&
    isChannelList(value.sendChannels) &&
    Number.isInteger(value.maxLength) &&
    (value.maxLength as number) >= 1 &&
    (value.maxLength as number) <= MAX_SEND_LENGTH &&
    (value.contacts === undefined || Array.isArray(value.contacts))
  );
}

// A bad contact, e.g. from a plugin older than this page, drops only that contact and keeps the other settings.
export function parseSettings(value: unknown): Omit<SettingsPayload, "type" | "seq"> | null {
  if (!isSettings(value)) return null;
  const settings = Array.isArray(value.contacts) ? { ...value, contacts: value.contacts.filter(isTellContact) } : value;
  return settings as unknown as Omit<SettingsPayload, "type" | "seq">;
}

function hasStrings(value: unknown, keys: string[]): value is Record<string, unknown> {
  return isObject(value) && keys.every((k) => typeof value[k] === "string");
}

function isTellContact(value: unknown): value is TellContact {
  return (
    isObject(value) &&
    isName(value.character) &&
    isName(value.characterWorld) &&
    isBoundedString(value.characterHash, MAX_HASH_LENGTH) &&
    isName(value.name) &&
    isName(value.world) &&
    isBoundedString(value.hash, MAX_HASH_LENGTH) &&
    isBoundedString(value.installId, MAX_NAME_LENGTH) &&
    isBoundedString(value.key, MAX_KEY_LENGTH)
  );
}

export function isTellBundle(value: unknown): value is TellBundle {
  return (
    isObject(value) &&
    typeof value.installPublicKey === "string" &&
    typeof value.issuedAt === "number" &&
    Array.isArray(value.entries) &&
    value.entries.every((e) => hasStrings(e, ["target", "key"]) && typeof e.push === "boolean")
  );
}

// ts may be ahead of our clock, the caller clamps it.
export function parseTellBody(value: unknown): TellBody | null {
  const valid =
    isObject(value) &&
    isBoundedString(value.id, MAX_NAME_LENGTH) &&
    isBoundedString(value.fromHash, MAX_HASH_LENGTH) &&
    isName(value.fromName) &&
    isName(value.fromWorld) &&
    isBoundedString(value.toHash, MAX_HASH_LENGTH) &&
    isName(value.toName) &&
    isName(value.toWorld) &&
    isBoundedString(value.text, MAX_TEXT_LENGTH) &&
    typeof value.ts === "number" &&
    Number.isFinite(value.ts) &&
    value.ts >= 0;
  return valid ? (value as unknown as TellBody) : null;
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
      return isBoundedString(value.requestId, MAX_NAME_LENGTH) &&
        typeof value.ok === "boolean" &&
        (value.error === undefined || value.error === null || isBoundedString(value.error, MAX_NAME_LENGTH))
        ? (value as unknown as SendResultPayload)
        : null;
    case "settings":
      return parseSettings(value) as SettingsPayload | null;
    default:
      return null;
  }
}

const SERVER_FRAME_TYPES = ["authOk", "authFail", "msg", "pluginStatus", "paired", "revoked", "error", "tell", "tellResult"];

export function parseServerFrame(text: string): ServerFrame | null {
  try {
    const value: unknown = JSON.parse(text);
    if (!isObject(value) || !SERVER_FRAME_TYPES.includes(value.t as string)) return null;
    if (value.t === "msg" && typeof value.payload !== "string") return null;
    if (value.t === "pluginStatus" && typeof value.online !== "boolean") return null;
    if (value.t === "tell" && !hasStrings(value, ["id", "from", "envelope", "fromKey"])) return null;
    if (value.t === "tellResult" && (typeof value.id !== "string" || typeof value.ok !== "boolean")) return null;
    return value as unknown as ServerFrame;
  } catch {
    return null;
  }
}
