import { describe, expect, it } from "vitest";
import type { AccountView } from "../core/accounts";
import { EMPTY_CHANNEL_PREFS, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import { subRows } from "./channels";
import { accountLink, chatTarget, initialPlace, parseLink, parsePlace, parseViews, pickTarget, remember, resolveChannel, resolveRow, validPlace } from "./place";

const acct = (deviceId: string, character?: string, status = "online", accountStatus = "active") =>
  ({ deviceId, label: deviceId, character, status: accountStatus, unread: 0, state: { status } }) as unknown as AccountView;
const chars = (a: AccountView) => (a.character ? [a.character, "Zed Alt"] : []);

const fc: CustomChannel = { id: "fc", character: "Alpha Beta", name: "FC", channels: ["freeCompany"] };
const social: CustomChannel = { id: "s", character: "Alpha Beta", name: "Social", channels: ["freeCompany", "say"] };
const prefs: ChannelPrefs = { ...EMPTY_CHANNEL_PREFS, custom: [fc, social] };

describe("stored place", () => {
  it("reads a place and ignores junk", () => {
    expect(parsePlace('{"deviceId":"a","character":"Alpha Beta"}')).toEqual({ deviceId: "a", character: "Alpha Beta" });
    expect(parsePlace('{"deviceId":"a","character":3}')).toEqual({ deviceId: "a", character: null });
    expect(parsePlace("{nope")).toBeNull();
    expect(parsePlace('"a"')).toBeNull();
    expect(parsePlace(null)).toBeNull();
  });

  it("reads remembered views and drops non-strings", () => {
    expect(parseViews('{"a|X":"fc","b":3}')).toEqual({ "a|X": "fc" });
    expect(parseViews("[1]")).toEqual({});
    expect(parseViews("{nope")).toEqual({});
  });

  it("only copies views that change", () => {
    const views = { "a|X": "fc" };
    expect(remember(views, "a|X", "fc")).toBe(views);
    expect(remember(views, "a|X", "s")).toEqual({ "a|X": "s" });
  });
});

describe("validPlace", () => {
  it("has no place without accounts", () => {
    expect(validPlace({ deviceId: "a", character: "X" }, [], chars)).toBeNull();
  });

  it("keeps a place on an existing client, even a character whose messages aren't loaded", () => {
    expect(validPlace({ deviceId: "a", character: "Old Alt" }, [acct("a", "Alpha Beta")], chars)).toEqual({ deviceId: "a", character: "Old Alt" });
  });

  it("fills in the logged-in character", () => {
    expect(validPlace({ deviceId: "a", character: null }, [acct("a", "Alpha Beta")], chars)).toEqual({ deviceId: "a", character: "Alpha Beta" });
  });

  it("falls back to the first usable client when the saved one is gone", () => {
    const accounts = [acct("p", undefined, "pending"), acct("b", "Beta Char")];
    expect(validPlace({ deviceId: "gone", character: "X" }, accounts, chars)).toEqual({ deviceId: "b", character: "Beta Char" });
    expect(validPlace(null, accounts, chars)).toEqual({ deviceId: "b", character: "Beta Char" });
  });

  it("has no character on a pending or removed client", () => {
    expect(validPlace({ deviceId: "p", character: "X" }, [acct("p", "X", "pending")], chars)).toEqual({ deviceId: "p", character: null });
    expect(validPlace({ deviceId: "r", character: "X" }, [acct("r", "X", "revoked", "revoked")], chars)).toEqual({ deviceId: "r", character: null });
  });
});

describe("links", () => {
  it("round trips a client and chat", () => {
    expect(parseLink(accountLink("a b", "t|Alpha Beta|A B@W").slice(1))).toEqual({ deviceId: "a b", chat: "t|Alpha Beta|A B@W" });
    expect(parseLink(accountLink("a").slice(1))).toEqual({ deviceId: "a", chat: null });
  });

  it("ignores other hashes and bad encoding", () => {
    expect(parseLink("#pair=ABCD")).toBeNull();
    expect(parseLink("#account=%E0%A4%A")).toBeNull();
  });

  it("starts on a linked client and character", () => {
    const accounts = [acct("a", "Alpha Beta"), acct("b", "Beta Char")];
    expect(initialPlace(accounts, "#account=b&chat=c%7CAlt%20Char%7Cparty", null, chars)).toEqual({ deviceId: "b", character: "Alt Char" });
    expect(initialPlace(accounts, "#account=b", '{"deviceId":"a","character":"Zed Alt"}', chars)).toEqual({ deviceId: "b", character: "Beta Char" });
  });

  it("restores the stored place without a link", () => {
    const accounts = [acct("a", "Alpha Beta"), acct("b", "Beta Char")];
    expect(initialPlace(accounts, "", '{"deviceId":"b","character":"Zed Alt"}', chars)).toEqual({ deviceId: "b", character: "Zed Alt" });
    expect(initialPlace(accounts, "#account=gone", null, chars)).toEqual({ deviceId: "a", character: "Alpha Beta" });
  });
});

describe("chatTarget", () => {
  it("opens tells in Tells and chat types in the first channel showing them", () => {
    expect(chatTarget(prefs, "t|Alpha Beta|A B@W")).toEqual({ character: "Alpha Beta", channelId: "tells", rowKey: "t|Alpha Beta|A B@W" });
    expect(chatTarget(prefs, "c|Alpha Beta|say")).toEqual({ character: "Alpha Beta", channelId: "s", rowKey: "c|Alpha Beta|say" });
  });

  it("still finds the character for a chat type no channel shows", () => {
    expect(chatTarget(prefs, "c|Alpha Beta|yell")).toEqual({ character: "Alpha Beta", channelId: undefined, rowKey: "c|Alpha Beta|yell" });
  });

  it("ignores keys that aren't messages", () => {
    expect(chatTarget(prefs, "x|Alpha Beta|fc")).toBeNull();
    expect(chatTarget(prefs, "nonsense")).toBeNull();
  });

  it("remembers the target's channel and row", () => {
    const picks = pickTarget({ server: {}, sub: {} }, "a", { character: "Alpha Beta", channelId: "s", rowKey: "c|Alpha Beta|say" });
    expect(picks).toEqual({ server: { "a|Alpha Beta": "s" }, sub: { "a|x|Alpha Beta|s": "c|Alpha Beta|say" } });
    const none = { server: {}, sub: {} };
    expect(pickTarget(none, "a", { character: "Alpha Beta", rowKey: "c|Alpha Beta|yell" })).toBe(none);
  });
});

describe("resolving views", () => {
  const tells: CustomChannel = { id: "tells", character: "Alpha Beta", name: "Tells", channels: ["tell"] };

  it("falls back to Tells when the open channel was deleted", () => {
    expect(resolveChannel([tells, fc], "s")).toBe(tells);
    expect(resolveChannel([tells, fc], "fc")).toBe(fc);
    expect(resolveChannel([], "fc")).toBeNull();
  });

  it("falls back to the first row", () => {
    const rows = subRows(social, []);
    expect(resolveRow(rows, "c|Alpha Beta|say")).toBe(rows[2]);
    expect(resolveRow(rows, "c|Alpha Beta|yell")).toBe(rows[0]);
    expect(resolveRow(rows, undefined)).toBe(rows[0]);
  });
});
