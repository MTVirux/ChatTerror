import { describe, expect, it } from "vitest";
import type { AccountManager } from "../core/accounts";
import { EMPTY_CHANNEL_PREFS, type ChannelPrefs } from "../core/channelPrefs";
import type { SendResult } from "../core/session";
import { chatRows, dmRow } from "./chatList";
import { createPendingSends, pendingIn, type PendingSend } from "./pending";

function fakeManager(results: SendResult[]) {
  const calls: string[] = [];
  const manager = {
    session: (deviceId: string) => ({
      send: async (_channel: string, text: string) => {
        calls.push(`${deviceId}:${text}`);
        return results.shift()!;
      },
    }),
  } as unknown as AccountManager;
  return { manager, calls };
}

describe("pending sends", () => {
  it("removes a send once it went out", async () => {
    const { manager } = fakeManager([{ ok: true }]);
    const pending = createPendingSends(manager);
    const done = pending.send("a", "say", "hi");
    expect(pending.list()).toMatchObject([{ deviceId: "a", text: "hi" }]);
    await done;
    expect(pending.list()).toEqual([]);
  });

  it("keeps a failed send with its error and retries it in place", async () => {
    const { manager, calls } = fakeManager([{ ok: false, error: undefined }, { ok: true }]);
    const pending = createPendingSends(manager);
    const seen: number[] = [];
    pending.subscribe((list) => seen.push(list.length));
    await pending.send("b", "say", "hello");
    const [failed] = pending.list();
    expect(failed.error).toBe("Message failed to send");

    await pending.retry(failed);
    expect(calls).toEqual(["b:hello", "b:hello"]);
    expect(pending.list()).toEqual([]);
    expect(seen.at(-1)).toBe(0);
  });

  it("dismisses a failed send", async () => {
    const { manager } = fakeManager([{ ok: false, error: undefined }]);
    const pending = createPendingSends(manager);
    await pending.send("a", "say", "x");
    pending.dismiss(pending.list()[0].localId);
    expect(pending.list()).toEqual([]);
  });
});

describe("pendingIn", () => {
  const prefs: ChannelPrefs = { ...EMPTY_CHANNEL_PREFS, custom: [{ id: "s", character: "Alpha Beta", name: "Social", channels: ["freeCompany", "say"] }] };
  const [group] = chatRows(prefs, "Alpha Beta", []);
  const dm = dmRow(prefs, "Alpha Beta", "A B@W");
  const send = (over: Partial<PendingSend>): PendingSend => ({ localId: 1, deviceId: "a", channel: "freeCompany", text: "hi", character: "Alpha Beta", ...over });

  it("shows a send in a group of its channel", () => {
    expect(pendingIn(send({}), "a", group)).toBe(true);
    expect(pendingIn(send({ channel: "party" }), "a", group)).toBe(false);
  });

  it("shows a tell only with its partner", () => {
    expect(pendingIn(send({ channel: "tell", target: "A B@W" }), "a", dm)).toBe(true);
    expect(pendingIn(send({ channel: "tell", target: "C D@W" }), "a", dm)).toBe(false);
  });

  it("keeps other clients and characters out", () => {
    expect(pendingIn(send({ deviceId: "b" }), "a", group)).toBe(false);
    expect(pendingIn(send({ character: "Other One" }), "a", group)).toBe(false);
  });

  it("shows every send of the client with no chat open", () => {
    expect(pendingIn(send({ channel: "party" }), "a", null)).toBe(true);
  });
});
