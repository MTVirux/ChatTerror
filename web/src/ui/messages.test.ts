import { describe, expect, it } from "vitest";
import type { FeedItem } from "../core/accounts";
import { feedKey } from "./feed";
import { buildRows, incomingAfter } from "./messages";

const MIN = 60_000;
const DAY = 24 * 60 * MIN;
function m(id: string, ts: number, over: Partial<FeedItem> = {}): FeedItem {
  return { deviceId: "a", id, ts, channel: "party", sender: "A B", senderWorld: "W", text: id, character: "Alpha Beta", outgoing: false, ...over };
}

describe("buildRows", () => {
  it("groups the same sender within five minutes", () => {
    const rows = buildRows([m("1", 0), m("2", 4 * MIN), m("3", 10 * MIN)], 0, false);
    expect(rows.map((r) => r.head)).toEqual([true, false, true]);
  });
  it("starts a new group on a different sender or account", () => {
    const rows = buildRows([m("1", 0), m("2", 1, { sender: "C D" }), m("3", 2, { sender: "C D", deviceId: "b" })], 0, true);
    expect(rows.map((r) => r.head)).toEqual([true, true, true]);
  });
  it("starts a new group on a different channel or direction", () => {
    const rows = buildRows([m("1", 0), m("2", 1, { channel: "say" }), m("3", 2, { channel: "say", outgoing: true })], 0, true);
    expect(rows.map((r) => r.head)).toEqual([true, true, true]);
  });
  it("starts a new group with a day divider when the day changes", () => {
    const start = new Date(2026, 0, 1, 23, 58).getTime();
    const rows = buildRows([m("1", start), m("2", start + 3 * MIN)], 0, false);
    expect(rows.map((r) => r.head)).toEqual([true, true]);
    expect(rows[1].day).toBeDefined();
  });
  it("labels the first row with its day and only day changes after it", () => {
    const rows = buildRows([m("1", 0), m("2", 1), m("3", DAY * 3)], 0, false);
    expect(rows.map((r) => r.day !== undefined)).toEqual([true, false, true]);
  });
  it("marks the first unread incoming message", () => {
    const rows = buildRows([m("1", 0), m("2", 1), m("3", 2, { outgoing: true }), m("4", 3)], 2, false);
    expect(rows.findIndex((r) => r.newMarker)).toBe(1);
  });
  it("marks the oldest incoming message when the count is larger than the list", () => {
    const rows = buildRows([m("1", 0, { outgoing: true }), m("2", 1)], 5, false);
    expect(rows.findIndex((r) => r.newMarker)).toBe(1);
  });
  it("adds no marker without unread", () => {
    expect(buildRows([m("1", 0)], 0, false).some((r) => r.newMarker)).toBe(false);
  });
});

describe("incomingAfter", () => {
  const items = [m("1", 0), m("2", 1), m("3", 2, { outgoing: true }), m("4", 3)];
  it("counts incoming messages newer than the given one", () => {
    expect(incomingAfter(items, feedKey(items[1]))).toBe(1);
    expect(incomingAfter(items, feedKey(items[0]))).toBe(2);
  });
  it("counts nothing when the message is unknown", () => {
    expect(incomingAfter(items, undefined)).toBe(0);
    expect(incomingAfter(items, "a:gone")).toBe(0);
  });
});
