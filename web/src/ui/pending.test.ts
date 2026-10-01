import { describe, expect, it } from "vitest";
import type { AccountManager } from "../core/accounts";
import type { SendResult } from "../core/session";
import { createPendingSends } from "./pending";

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
