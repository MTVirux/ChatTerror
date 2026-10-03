import type { FeedItem } from "../core/accounts";
import { feedKey } from "./feed";
import { dayLabel } from "./format";

const GROUP_GAP_MS = 5 * 60 * 1000;

export interface Row {
  item: FeedItem;
  head: boolean;
  day?: string;
  newMarker?: boolean;
}

function sameGroup(prev: FeedItem, item: FeedItem): boolean {
  return prev.sender === item.sender && prev.outgoing === item.outgoing && prev.channel === item.channel &&
    prev.deviceId === item.deviceId && prev.character === item.character && item.ts - prev.ts <= GROUP_GAP_MS;
}

export function buildRows(items: FeedItem[], unreadCount: number): Row[] {
  let prev: FeedItem | undefined;
  let prevDay: string | undefined;
  const rows = items.map((item): Row => {
    const day = dayLabel(item.ts);
    const newDay = day !== prevDay;
    const head = !prev || newDay || !sameGroup(prev, item);
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
