import type { FeedItem } from "../core/accounts";
import type { ChatItem } from "../core/protocol";
import { feedKey } from "./feed";
import { dayLabel } from "./format";
import type { PendingSend } from "./pending";

const GROUP_GAP_MS = 5 * 60 * 1000;

export interface Row {
  item: FeedItem;
  head: boolean;
  day?: string;
  newMarker?: boolean;
}

type RunPart = Pick<FeedItem, "deviceId" | "channel" | "outgoing" | "ts">;

function sameRun(prev: RunPart, item: RunPart): boolean {
  return prev.outgoing === item.outgoing && prev.channel === item.channel && prev.deviceId === item.deviceId &&
    item.ts - prev.ts <= GROUP_GAP_MS && new Date(prev.ts).toDateString() === new Date(item.ts).toDateString();
}

function sameGroup(prev: FeedItem, item: FeedItem): boolean {
  return prev.sender === item.sender && prev.character === item.character && sameRun(prev, item);
}

export function buildRows(items: FeedItem[], unreadCount: number): Row[] {
  let prev: FeedItem | undefined;
  let prevDay: string | undefined;
  const rows = items.map((item): Row => {
    const day = dayLabel(item.ts);
    const newDay = day !== prevDay;
    const head = !prev || !sameGroup(prev, item);
    prev = item;
    prevDay = day;
    return newDay ? { item, head, day } : { item, head };
  });

  let left = unreadCount;
  let marker: Row | undefined;
  for (let i = rows.length - 1; i >= 0 && left > 0; i--) {
    if (rows[i].item.outgoing) continue;
    marker = rows[i];
    left--;
  }
  if (marker) marker.newMarker = true;
  return rows;
}

export function incomingAfter(items: FeedItem[], key: string | undefined): number {
  const index = key === undefined ? -1 : items.findIndex((i) => feedKey(i) === key);
  if (index < 0) return 0;
  return items.slice(index + 1).filter((i) => !i.outgoing).length;
}

// System lines like echo, errors or sales have no sender.
export function bubbleSide(item: ChatItem): "in" | "out" | "system" {
  if (item.outgoing) return "out";
  return item.sender ? "in" : "system";
}

// Pending sends have no sender or time yet, so they're matched as own messages sent now.
export function pendingHeads(items: FeedItem[], pending: PendingSend[], now: number): boolean[] {
  let prev: RunPart | undefined = items.at(-1);
  return pending.map((p) => {
    const part = { deviceId: p.deviceId, channel: p.channel, outgoing: true, ts: now };
    const head = !prev || !sameRun(prev, part);
    prev = part;
    return head;
  });
}
