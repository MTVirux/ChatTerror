import { describe, expect, it } from "vitest";
import type { FeedItem } from "../core/accounts";
import { EMPTY_CHANNEL_PREFS, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import { chatForKey, chatRows, dmRow, findChat, previewText, visibleGroups } from "./chatList";

function msg(over: Partial<FeedItem>): FeedItem {
  return { deviceId: "a", id: Math.random().toString(), ts: 1, channel: "party", sender: "Y'shtola Rhul", senderWorld: "Twintania", text: "hi", character: "Alpha Beta", outgoing: false, ...over };
}

const fc: CustomChannel = { id: "fc", character: "Alpha Beta", name: "FC", channels: ["freeCompany"] };
const raid: CustomChannel = { id: "r", character: "Alpha Beta", name: "Raid", channels: ["linkshell1", "crossLinkshell2", "tell"] };
const oldTells: CustomChannel = { id: "tells", character: "Alpha Beta", name: "Tells", channels: ["tell"] };
const prefs: ChannelPrefs = { ...EMPTY_CHANNEL_PREFS, custom: [oldTells, fc, raid, { ...fc, character: "Other One" }] };

describe("visibleGroups", () => {
  it("hides groups left with only tells and strips tells from the rest", () => {
    expect(visibleGroups(prefs, "Alpha Beta")).toEqual([fc, { ...raid, channels: ["linkshell1", "crossLinkshell2"] }]);
  });
});

describe("chatRows", () => {
  const items = [
    msg({ channel: "freeCompany", ts: 10, sender: "Hoshi Ka" }),
    msg({ channel: "tell", sender: "Mira Sol", senderWorld: "Lich", ts: 30 }),
    msg({ channel: "tell", sender: "Tomo Rinn", senderWorld: "Odin", ts: 5 }),
    msg({ channel: "linkshell1", ts: 20 }),
  ];

  it("lists DMs and groups newest first, empty groups last", () => {
    const empty = { ...prefs, custom: [...prefs.custom, { id: "e", character: "Alpha Beta", name: "Empty", channels: ["say"] } as CustomChannel] };
    expect(chatRows(empty, "Alpha Beta", items).map((r) => r.name)).toEqual(["Mira Sol", "Raid", "FC", "Tomo Rinn", "Empty"]);
  });

  it("puts pins first in pin order", () => {
    const pinned = { ...prefs, pinned: ["x|Alpha Beta|fc", "t|Alpha Beta|Tomo Rinn@Odin"] };
    expect(chatRows(pinned, "Alpha Beta", items).map((r) => r.name)).toEqual(["FC", "Tomo Rinn", "Mira Sol", "Raid"]);
  });

  it("keeps a pinned DM with no loaded messages", () => {
    const pinned = { ...prefs, pinned: ["t|Alpha Beta|Kai Rho@Phoenix"] };
    const row = chatRows(pinned, "Alpha Beta", [])[0];
    expect(row).toMatchObject({ key: "t|Alpha Beta|Kai Rho@Phoenix", kind: "dm", name: "Kai Rho", world: "Phoenix", pinned: true });
    expect(row.last).toBeUndefined();
  });

  it("marks muted rows", () => {
    const muted = { ...prefs, muted: ["x|Alpha Beta|r"] };
    expect(chatRows(muted, "Alpha Beta", items).find((r) => r.name === "Raid")?.muted).toBe(true);
  });
});

describe("dmRow", () => {
  it("handles a partner with no known world", () => {
    expect(dmRow(prefs, "Alpha Beta", "Thancred Waters")).toMatchObject({ key: "t|Alpha Beta|Thancred Waters", name: "Thancred Waters", world: undefined });
  });
});

describe("previewText", () => {
  const group = (last: FeedItem) => ({ ...dmRow(prefs, "Alpha Beta", "X Y@W"), kind: "group" as const, last });
  it("names the sender in groups, you for own messages, and nothing for DMs and system lines", () => {
    expect(previewText(group(msg({ sender: "Hoshi Ka", text: "maps?" })))).toBe("Hoshi: maps?");
    expect(previewText(group(msg({ outgoing: true, text: "omw" })))).toBe("You: omw");
    expect(previewText(dmRow(prefs, "Alpha Beta", "X Y@W", msg({ channel: "tell", text: "hey" })))).toBe("hey");
    expect(previewText(group(msg({ sender: "", channel: "echo", text: "note" })))).toBe("note");
    expect(previewText(dmRow(prefs, "Alpha Beta", "X Y@W"))).toBe("");
  });
});

describe("chatForKey", () => {
  const rows = chatRows(prefs, "Alpha Beta", []);
  it("opens DMs directly and chat types in the first group showing them", () => {
    expect(chatForKey(rows, prefs, "t|Alpha Beta|A B@W")).toBe("t|Alpha Beta|A B@W");
    expect(chatForKey(rows, prefs, "c|Alpha Beta|crossLinkshell2")).toBe("x|Alpha Beta|r");
  });
  it("has nothing for a chat type no group shows", () => {
    expect(chatForKey(rows, prefs, "c|Alpha Beta|say")).toBeNull();
  });
});

describe("findChat", () => {
  const rows = chatRows(prefs, "Alpha Beta", []);
  it("finds rows, makes empty DMs and drops deleted groups", () => {
    expect(findChat(rows, "Alpha Beta", "x|Alpha Beta|fc", prefs)?.name).toBe("FC");
    expect(findChat(rows, "Alpha Beta", "t|Alpha Beta|New Pal@Lich", prefs)).toMatchObject({ kind: "dm", name: "New Pal" });
    expect(findChat(rows, "Alpha Beta", "x|Alpha Beta|gone", prefs)).toBeNull();
    expect(findChat(rows, "Alpha Beta", "t|Other One|New Pal@Lich", prefs)).toBeNull();
    expect(findChat(rows, "Alpha Beta", null, prefs)).toBeNull();
  });
});
