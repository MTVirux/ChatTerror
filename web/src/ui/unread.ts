import type { AccountManager } from "../core/accounts";
import { channelKey, itemChannelRef } from "./channels";

export interface UnreadSummary {
  unread: boolean;
  tells: number;
}

export type OpenChannel = { deviceId: string; key: string } | "home" | null;

export interface UnreadTracker {
  count(deviceId: string, key: string): number;
  summary(deviceId: string): UnreadSummary;
  total(deviceIds: string[]): UnreadSummary;
  setOpen(open: OpenChannel): void;
  subscribe(cb: () => void): () => void;
  close(): void;
}

// Muted channels still count, so unmuting shows what was missed, but stay out of the summaries.
export function createUnreadTracker(
  manager: Pick<AccountManager, "onMessages">,
  isMuted: (deviceId: string, key: string) => boolean = () => false,
): UnreadTracker {
  const counts = new Map<string, Map<string, number>>();
  const listeners = new Set<() => void>();
  let open: OpenChannel = null;

  const of = (deviceId: string) => {
    let m = counts.get(deviceId);
    if (!m) counts.set(deviceId, (m = new Map()));
    return m;
  };
  const notify = () => listeners.forEach((l) => l());
  const summarize = (deviceId: string): UnreadSummary => {
    let unread = false;
    let tells = 0;
    for (const [key, n] of counts.get(deviceId) ?? []) {
      if (n <= 0 || isMuted(deviceId, key)) continue;
      unread = true;
      if (key.startsWith("t|")) tells += n;
    }
    return { unread, tells };
  };

  const off = manager.onMessages((items) => {
    if (open === "home") return;
    let changed = false;
    for (const item of items) {
      if (item.outgoing) continue;
      const key = channelKey(itemChannelRef(item));
      if (open && open.deviceId === item.deviceId && open.key === key) continue;
      const m = of(item.deviceId);
      m.set(key, (m.get(key) ?? 0) + 1);
      changed = true;
    }
    if (changed) notify();
  });

  return {
    count: (deviceId, key) => counts.get(deviceId)?.get(key) ?? 0,
    summary: summarize,
    total(deviceIds) {
      let unread = false;
      let tells = 0;
      for (const deviceId of deviceIds) {
        const s = summarize(deviceId);
        unread ||= s.unread;
        tells += s.tells;
      }
      return { unread, tells };
    },
    setOpen(next) {
      open = next;
      if (next && next !== "home" && (counts.get(next.deviceId)?.get(next.key) ?? 0) > 0) {
        counts.get(next.deviceId)!.set(next.key, 0);
        notify();
      }
    },
    subscribe(cb) {
      listeners.add(cb);
      return () => listeners.delete(cb);
    },
    close() {
      off();
      listeners.clear();
    },
  };
}
