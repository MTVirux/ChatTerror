import { describe, expect, it } from "vitest";
import type { AccountView } from "../core/accounts";
import { accountLink, chatTarget, initialPlace, parseLink, parsePlace, parseViews, remember, validPlace } from "./place";

const acct = (deviceId: string, character?: string, status = "online", accountStatus = "active") =>
  ({ deviceId, label: deviceId, character, status: accountStatus, unread: 0, state: { status } }) as unknown as AccountView;
const chars = (a: AccountView) => (a.character ? [a.character, "Zed Alt"] : []);

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
  it("reads the character out of a notification chat key", () => {
    expect(chatTarget("t|Alpha Beta|A B@W")).toEqual({ character: "Alpha Beta", chat: "t|Alpha Beta|A B@W" });
    expect(chatTarget("c|Alpha Beta|say")).toEqual({ character: "Alpha Beta", chat: "c|Alpha Beta|say" });
    expect(chatTarget("x|Alpha Beta|fc")).toBeNull();
    expect(chatTarget("junk")).toBeNull();
  });
});
