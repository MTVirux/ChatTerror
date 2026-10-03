import { describe, expect, it } from "vitest";
import type { AccountView } from "../core/accounts";
import { EMPTY_CHANNEL_PREFS, withDefaultChannels, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import type { ChatItem } from "../core/protocol";
import { channelFor, channelSlug, charactersOf, customColor, itemKey, keyCharacter, partnersOf, railChannels, showsChatTags, subRows } from "./channels";

function msg(over: Partial<ChatItem>): ChatItem {
  return { id: Math.random().toString(), ts: 1, channel: "party", sender: "Y'shtola Rhul", senderWorld: "Twintania", text: "hi", character: "Alpha Beta", outgoing: false, ...over };
}

const tells: CustomChannel = { id: "tells", character: "Alpha Beta", name: "Tells", channels: ["tell"] };
const fc: CustomChannel = { id: "fc", character: "Alpha Beta", name: "FC", channels: ["freeCompany"] };
const social: CustomChannel = { id: "s", character: "Alpha Beta", name: "Social", channels: ["linkshell1", "say"] };
const mixed: CustomChannel = { id: "m", character: "Alpha Beta", name: "Mixed", channels: ["freeCompany", "tell"] };
const prefs: ChannelPrefs = { ...EMPTY_CHANNEL_PREFS, custom: [fc, social, tells, { ...fc, character: "Other One" }] };

describe("keys", () => {
  it("puts both directions of a tell in one key", () => {
    const incoming = msg({ channel: "tell", sender: "Thancred Waters", senderWorld: "Gilgamesh" });
    const outgoing = msg({ channel: "tell", sender: "Thancred Waters@Gilgamesh", senderWorld: undefined, outgoing: true });
    expect(itemKey(incoming)).toBe("t|Alpha Beta|Thancred Waters@Gilgamesh");
    expect(itemKey(outgoing)).toBe(itemKey(incoming));
  });

  it("keeps a tell partner with an unknown world", () => {
    expect(itemKey(msg({ channel: "tell", sender: "Thancred Waters", senderWorld: undefined }))).toBe("t|Alpha Beta|Thancred Waters");
  });

  it("reads the character out of item, row and custom keys", () => {
    expect(keyCharacter("c|Alpha Beta|party")).toBe("Alpha Beta");
    expect(keyCharacter("t|Alpha Beta|A B@W")).toBe("Alpha Beta");
    expect(keyCharacter("x|Alpha Beta|fc")).toBe("Alpha Beta");
    expect(keyCharacter("nonsense")).toBeUndefined();
    expect(keyCharacter("q|Alpha Beta|x")).toBeUndefined();
  });

  it("slugs channel labels", () => {
    expect(channelSlug("freeCompany")).toBe("fc");
    expect(channelSlug("linkshell1")).toBe("ls1");
  });
});

describe("railChannels", () => {
  it("puts Tells first, then the character's channels in creation order", () => {
    expect(railChannels(prefs, "Alpha Beta")).toEqual([tells, fc, social]);
  });

  it("applies the saved order and appends channels it doesn't know", () => {
    const ordered = { ...prefs, order: { "Alpha Beta": ["x|Alpha Beta|s", "t|Alpha Beta|A B@W", "x|Alpha Beta|gone"] } };
    expect(railChannels(ordered, "Alpha Beta")).toEqual([tells, social, fc]);
  });

  it("keeps Tells first even when the saved order moves it", () => {
    const ordered = { ...prefs, order: { "Alpha Beta": ["x|Alpha Beta|fc", "x|Alpha Beta|tells"] } };
    expect(railChannels(ordered, "Alpha Beta")).toEqual([tells, fc, social]);
  });

  it("handles characters named like Object.prototype keys", () => {
    expect(railChannels(prefs, "__proto__")).toEqual([]);
  });
});

describe("channelFor", () => {
  it("finds the first channel showing a message", () => {
    expect(channelFor(prefs, "Alpha Beta", "t|Alpha Beta|A B@W")).toEqual(tells);
    expect(channelFor(prefs, "Alpha Beta", "c|Alpha Beta|say")).toEqual(social);
    expect(channelFor(prefs, "Alpha Beta", "c|Alpha Beta|yell")).toBeUndefined();
  });
});

describe("partnersOf", () => {
  const items = [
    msg({ channel: "tell", sender: "A B", senderWorld: "W", ts: 10 }),
    msg({ channel: "tell", sender: "C D", senderWorld: "W", ts: 30 }),
    msg({ channel: "tell", sender: "A B@W", senderWorld: undefined, outgoing: true, ts: 20 }),
    msg({ channel: "tell", sender: "E F", senderWorld: "W", ts: 40, character: "Other One" }),
    msg({ channel: "party", ts: 50 }),
  ];

  it("lists one character's partners newest first", () => {
    expect(partnersOf(items, "Alpha Beta", [])).toEqual(["C D@W", "A B@W"]);
  });

  it("puts pinned partners first", () => {
    expect(partnersOf(items, "Alpha Beta", ["t|Alpha Beta|A B@W", "t|Other One|E F@W"])).toEqual(["A B@W", "C D@W"]);
  });
});

describe("subRows", () => {
  it("gives a single chat type one row", () => {
    expect(subRows(fc, [])).toEqual([{ key: "x|Alpha Beta|fc", kind: "all", label: "fc", hash: true }]);
  });

  it("lists every chat type of a mixed channel after all", () => {
    expect(subRows(social, []).map((r) => [r.key, r.kind, r.label])).toEqual([
      ["x|Alpha Beta|s", "all", "all"],
      ["c|Alpha Beta|linkshell1", "type", "ls1"],
      ["c|Alpha Beta|say", "type", "say"],
    ]);
  });

  it("lists partners under All tells", () => {
    expect(subRows(tells, ["A B@W"])).toEqual([
      { key: "x|Alpha Beta|tells", kind: "all", label: "All tells", hash: false },
      { key: "t|Alpha Beta|A B@W", kind: "partner", label: "A B", hash: false, partner: "A B@W" },
    ]);
  });

  it("shows the chat type and partners of a channel with one type and tells", () => {
    expect(subRows(mixed, ["A B@W"]).map((r) => r.kind)).toEqual(["all", "type", "partner"]);
  });
});

describe("showsChatTags", () => {
  it("tags the all view of a channel with more than one chat", () => {
    expect(showsChatTags(social, subRows(social, [])[0])).toBe(true);
    expect(showsChatTags(mixed, subRows(mixed, [])[0])).toBe(true);
  });

  it("skips single chat channels and single chat rows", () => {
    expect(showsChatTags(fc, subRows(fc, [])[0])).toBe(false);
    expect(showsChatTags(tells, subRows(tells, [])[0])).toBe(false);
    expect(showsChatTags(social, subRows(social, [])[1])).toBe(false);
    expect(showsChatTags(mixed, subRows(mixed, ["A B@W"])[2])).toBe(false);
  });
});

describe("customColor", () => {
  it("uses the first non-tell chat type's color", () => {
    expect(customColor(mixed)).toBe("var(--ch-freeCompany)");
    expect(customColor(tells)).toBe("var(--ch-tell)");
    expect(customColor(undefined)).toBe("var(--muted)");
  });
});

describe("charactersOf", () => {
  const account = (character: string | undefined, channelPrefs: ChannelPrefs) => ({ deviceId: "a", label: "a", character, state: { channelPrefs } }) as unknown as AccountView;

  it("puts the logged-in character first and the rest by name", () => {
    const seeded = withDefaultChannels(withDefaultChannels(EMPTY_CHANNEL_PREFS, "Zed Alt"), "Alpha Beta");
    expect(charactersOf(account("Mid Char", seeded), [msg({ character: "Beta Alt" })])).toEqual(["Mid Char", "Alpha Beta", "Beta Alt", "Zed Alt"]);
  });

  it("lists known characters when nobody is logged in", () => {
    expect(charactersOf(account(undefined, prefs))).toEqual(["Alpha Beta", "Other One"]);
  });

  it("gives nothing for a fresh pairing", () => {
    expect(charactersOf(account(undefined, EMPTY_CHANNEL_PREFS))).toEqual([]);
  });
});
