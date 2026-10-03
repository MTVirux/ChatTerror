import { describe, expect, it } from "vitest";
import { isChatItem, parsePluginPayload, type ChatItem } from "./protocol";

const item: ChatItem = { id: "a1", ts: 1_700_000_000_000, channel: "say", sender: "Y'shtola Rhul", senderWorld: "Twintania", text: "hi", character: "Alpha Beta", outgoing: false };

const settings = { type: "settings", seq: 1, character: "Alpha Beta", relayChannels: ["say"], sendChannels: ["say"], maxLength: 500 };

describe("isChatItem", () => {
  it("accepts a normal item", () => {
    expect(isChatItem(item)).toBe(true);
    expect(isChatItem({ ...item, senderWorld: undefined, character: "" })).toBe(true);
  });

  it("rejects timestamps that are not a sane epoch in ms", () => {
    for (const ts of [1e300, NaN, Infinity, -Infinity, -1, Date.now() + 2 * 24 * 60 * 60 * 1000, "1"]) {
      expect(isChatItem({ ...item, ts })).toBe(false);
    }
  });

  it("rejects non-string or oversized fields", () => {
    expect(isChatItem({ ...item, character: 42 })).toBe(false);
    expect(isChatItem({ ...item, sender: null })).toBe(false);
    expect(isChatItem({ ...item, senderWorld: 1 })).toBe(false);
    expect(isChatItem({ ...item, text: "x".repeat(5000) })).toBe(false);
    expect(isChatItem({ ...item, character: "x".repeat(65) })).toBe(false);
  });

  it("rejects the channel key delimiter in names", () => {
    expect(isChatItem({ ...item, character: "Alpha|Beta" })).toBe(false);
    expect(isChatItem({ ...item, sender: "a|b" })).toBe(false);
  });
});

describe("parsePluginPayload", () => {
  it("rejects a chat or backlog with a bad item", () => {
    expect(parsePluginPayload({ type: "chat", seq: 1, item: { ...item, ts: 1e300 } })).toBeNull();
    expect(parsePluginPayload({ type: "backlog", seq: 1, items: [item, { ...item, ts: NaN }], done: true })).toBeNull();
    expect(parsePluginPayload({ type: "backlog", seq: 1, items: [item], done: true })).not.toBeNull();
  });

  it("validates settings", () => {
    expect(parsePluginPayload(settings)).not.toBeNull();
    expect(parsePluginPayload({ ...settings, character: undefined })).not.toBeNull();
    expect(parsePluginPayload({ ...settings, character: 42 })).toBeNull();
    expect(parsePluginPayload({ ...settings, character: "a|b" })).toBeNull();
    for (const maxLength of [0, -5, 1.5, 1e9, NaN, "500"]) {
      expect(parsePluginPayload({ ...settings, maxLength })).toBeNull();
    }
  });

  it("requires the paired install id and key on every contact", () => {
    const contact = { character: "A B", characterWorld: "Lich", characterHash: "h1", name: "C D", world: "Lich", hash: "h2", installId: "i1", key: "k1" };
    expect(parsePluginPayload({ ...settings, contacts: [contact] })).not.toBeNull();
    expect(parsePluginPayload({ ...settings, contacts: [{ ...contact, installId: undefined }] })).toBeNull();
    expect(parsePluginPayload({ ...settings, contacts: [{ ...contact, key: undefined }] })).toBeNull();
    expect(parsePluginPayload({ ...settings, contacts: [{ ...contact, installId: 5 }] })).toBeNull();
  });

  it("validates a send result error", () => {
    const result = { type: "sendResult", seq: 1, requestId: "r", ok: false };
    expect(parsePluginPayload({ ...result, error: "busy" })).not.toBeNull();
    expect(parsePluginPayload(result)).not.toBeNull();
    expect(parsePluginPayload({ ...result, error: 5 })).toBeNull();
    expect(parsePluginPayload({ ...result, error: { toString: "x" } })).toBeNull();
  });
});
