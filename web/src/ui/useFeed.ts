import { useEffect, useState } from "preact/hooks";
import type { AccountManager, FeedItem } from "../core/accounts";
import { mergeFeed } from "./feed";

const HISTORY = 300;
const MAX_IN_MEMORY = 3000;

// Messages for one account, or every account when deviceId is null.
export function useFeed(manager: AccountManager, deviceId: string | null, epoch: number): FeedItem[] {
  const [items, setItems] = useState<FeedItem[]>([]);
  useEffect(() => {
    let alive = true;
    setItems([]);
    const off = manager.onMessages((incoming) => {
      const mine = deviceId ? incoming.filter((i) => i.deviceId === deviceId) : incoming;
      if (mine.length > 0) setItems((prev) => mergeFeed(prev, mine, MAX_IN_MEMORY));
    });
    const history = deviceId
      ? (manager.session(deviceId)?.loadHistory(HISTORY) ?? Promise.resolve([])).then((list) => list.map((i) => ({ ...i, deviceId })))
      : manager.loadMerged(HISTORY);
    history.then((list) => {
      if (alive) setItems((prev) => mergeFeed(list, prev, MAX_IN_MEMORY));
    });
    return () => {
      alive = false;
      off();
    };
  }, [manager, deviceId, epoch]);
  return items;
}
