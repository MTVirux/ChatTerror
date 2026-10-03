import { useEffect, useState } from "preact/hooks";
import type { AccountManager, FeedItem } from "../core/accounts";
import { mergeFeed } from "./feed";

const HISTORY = 300;
const MAX_IN_MEMORY = 3000;

interface Feed {
  key: string;
  items: FeedItem[];
  loaded: boolean;
}

// Messages for one account.
export function useFeed(manager: AccountManager, deviceId: string, epoch: number): { items: FeedItem[]; loaded: boolean } {
  const key = `${deviceId}|${epoch}`;
  const [feed, setFeed] = useState<Feed>({ key, items: [], loaded: false });
  useEffect(() => {
    let alive = true;
    setFeed({ key, items: [], loaded: false });
    const add = (list: FeedItem[], loaded: boolean) => {
      if (!alive) return;
      setFeed((prev) => {
        const same = prev.key === key;
        return { key, items: mergeFeed(same ? prev.items : [], list, MAX_IN_MEMORY), loaded: loaded || (same && prev.loaded) };
      });
    };
    const off = manager.onMessages((incoming) => {
      const mine = incoming.filter((i) => i.deviceId === deviceId);
      if (mine.length > 0) add(mine, false);
    });
    const history = (manager.session(deviceId)?.loadHistory(HISTORY) ?? Promise.resolve([])).then((list) => list.map((i) => ({ ...i, deviceId })));
    history.then((list) => add(list, true), () => add([], true));
    return () => {
      alive = false;
      off();
    };
  }, [manager, key]);
  // The previous account's messages must not show for the render before the effect runs.
  return feed.key === key ? feed : { items: [], loaded: false };
}
