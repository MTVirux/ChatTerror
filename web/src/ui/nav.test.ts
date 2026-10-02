import { describe, expect, it } from "vitest";
import type { AccountView } from "../core/accounts";
import type { Category } from "./channels";
import { initialNav, navTo, parseLastChannels, rememberChannel, resolveChannel, serializeNav, validNav } from "./nav";

const acct = (deviceId: string) => ({ deviceId, label: deviceId, status: "active", unread: 0, state: {} }) as unknown as AccountView;

describe("nav", () => {
  it("goes to add without accounts", () => {
    expect(validNav({ server: "a", channel: "x" }, [])).toEqual({ server: "add", channel: null });
    expect(initialNav([], "", null)).toEqual({ server: "add", channel: null });
  });
  it("keeps add", () => {
    expect(validNav({ server: "add", channel: null }, [acct("a")])).toEqual({ server: "add", channel: null });
  });
  it("collapses home to the only account", () => {
    expect(validNav({ server: "home", channel: null }, [acct("a")])).toEqual({ server: "a", channel: null });
    expect(validNav({ server: "home", channel: null }, [acct("a"), acct("b")])).toEqual({ server: "home", channel: null });
  });
  it("falls back to the first account when the saved one is gone", () => {
    expect(validNav({ server: "gone", channel: "c|X|party" }, [acct("a")])).toEqual({ server: "a", channel: null });
  });
  it("keeps a valid channel key", () => {
    expect(validNav({ server: "a", channel: "c|X|party" }, [acct("a")])).toEqual({ server: "a", channel: "c|X|party" });
  });
  it("prefers pair links and notification links", () => {
    expect(initialNav([acct("a")], "#pair=ABCD", null)).toEqual({ server: "add", channel: null });
    expect(initialNav([acct("a"), acct("b")], "#account=b", serializeNav({ server: "a", channel: "c|X|party" }))).toEqual({ server: "b", channel: null });
    expect(initialNav([acct("a")], "#account=gone", null)).toEqual({ server: "a", channel: null });
  });
  it("restores the stored nav and ignores bad JSON", () => {
    const stored = serializeNav({ server: "b", channel: "c|X|party" });
    expect(initialNav([acct("a"), acct("b")], "", stored)).toEqual({ server: "b", channel: "c|X|party" });
    expect(initialNav([acct("a")], "", "{nope")).toEqual({ server: "a", channel: null });
    expect(initialNav([acct("a")], "", '"just a string"')).toEqual({ server: "a", channel: null });
  });
  it("starts on the first account without a stored nav", () => {
    expect(initialNav([acct("a"), acct("b")], "", null)).toEqual({ server: "a", channel: null });
  });
});

describe("resolveChannel", () => {
  const tree = [
    { character: "Alpha Beta", active: true, chats: ["party", "say"], tells: [], lastTs: 0 },
    { character: "Old Char", active: false, chats: [], tells: [{ partner: "Y'shtola Rhul@Twintania", ts: 1 }], lastTs: 1 },
  ] as Category[];

  it("keeps a channel that exists", () => {
    expect(resolveChannel("c|Alpha Beta|say", tree)).toEqual({ kind: "chat", character: "Alpha Beta", channel: "say" });
    expect(resolveChannel("t|Old Char|Y'shtola Rhul@Twintania", tree)).toEqual({ kind: "tell", character: "Old Char", partner: "Y'shtola Rhul@Twintania" });
  });
  it("falls back to the first channel when the saved one is gone or missing", () => {
    const first = { kind: "chat", character: "Alpha Beta", channel: "party" };
    expect(resolveChannel(null, tree)).toEqual(first);
    expect(resolveChannel("c|Gone Char|party", tree)).toEqual(first);
    expect(resolveChannel("c|Old Char|party", tree)).toEqual(first);
    expect(resolveChannel("nonsense", tree)).toEqual(first);
  });
  it("gives nothing for an empty tree", () => {
    expect(resolveChannel("c|Alpha Beta|say", [])).toBeNull();
  });
  it("opens a notification link on that account's last channel", () => {
    expect(initialNav([acct("a"), acct("b")], "#account=b", null, { b: "c|X|party" })).toEqual({ server: "b", channel: "c|X|party" });
  });
});

describe("last channels", () => {
  it("goes to a server's last channel", () => {
    expect(navTo("a", { a: "c|X|party" })).toEqual({ server: "a", channel: "c|X|party" });
    expect(navTo("b", { a: "c|X|party" })).toEqual({ server: "b", channel: null });
    expect(navTo("home", { home: "c|X|party" })).toEqual({ server: "home", channel: null });
  });
  it("remembers account channels only", () => {
    expect(rememberChannel({}, { server: "a", channel: "c|X|party" })).toEqual({ a: "c|X|party" });
    const last = { a: "c|X|party" };
    expect(rememberChannel(last, { server: "a", channel: null })).toBe(last);
    expect(rememberChannel(last, { server: "home", channel: null })).toBe(last);
    expect(rememberChannel(last, { server: "a", channel: "c|X|party" })).toBe(last);
  });
  it("reads stored channels and ignores junk", () => {
    expect(parseLastChannels('{"a":"c|X|party","b":3}')).toEqual({ a: "c|X|party" });
    expect(parseLastChannels("{nope")).toEqual({});
    expect(parseLastChannels(null)).toEqual({});
    expect(parseLastChannels("[1]")).toEqual({});
  });
});
