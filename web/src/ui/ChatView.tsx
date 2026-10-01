import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "preact/hooks";
import type { AccountManager, AccountView, FeedItem } from "../core/accounts";
import { ALL_CHANNELS, type ChatChannel, type ChatItem } from "../core/protocol";
import type { SessionState } from "../core/session";
import { Composer } from "./Composer";
import { defaultSendAccount, feedKey, mergeFeed } from "./feed";
import type { PendingSend, PendingSends } from "./pending";
import { SettingsView } from "./SettingsView";
import { channelColor, channelLabel, dayLabel, STATUS_LABELS, tellPartner, timeOfDay } from "./format";

const MAX_IN_MEMORY = 3000;
const GROUP_GAP_MS = 5 * 60 * 1000;

export type Tab = { kind: "all" } | { kind: "channel"; channel: ChatChannel } | { kind: "tell"; partner: string };

function tabKey(tab: Tab): string {
  if (tab.kind === "channel") return `ch:${tab.channel}`;
  if (tab.kind === "tell") return `tell:${tab.partner}`;
  return "all";
}

function itemTabKey(item: ChatItem): string {
  return item.channel === "tell" ? `tell:${tellPartner(item)}` : `ch:${item.channel}`;
}

export function ChatView({ manager, accounts, accountId, sends, pending, onOpenAccount, onAddAccount }: {
  manager: AccountManager;
  accounts: AccountView[];
  accountId: string | null;
  sends: PendingSends;
  pending: PendingSend[];
  onOpenAccount: (deviceId: string) => void;
  onAddAccount: () => void;
}) {
  const [items, setItems] = useState<FeedItem[]>([]);
  const [tab, setTab] = useState<Tab>({ kind: "all" });
  const [unread, setUnread] = useState<Record<string, number>>({});
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [epoch, setEpoch] = useState(0);
  const [sendAccount, setSendAccount] = useState<string | undefined>(undefined);
  const activeKey = useRef("all");
  activeKey.current = tabKey(tab);
  const merged = accountId === null;

  useEffect(() => {
    let alive = true;
    const off = manager.onMessages((incoming) => {
      const mine = accountId ? incoming.filter((i) => i.deviceId === accountId) : incoming;
      if (mine.length === 0) return;
      setItems((prev) => mergeFeed(prev, mine, MAX_IN_MEMORY));
      setUnread((prev) => {
        const next = { ...prev };
        for (const item of mine) {
          const key = itemTabKey(item);
          if (!item.outgoing && key !== activeKey.current) next[key] = (next[key] ?? 0) + 1;
        }
        return next;
      });
    });
    const history = accountId
      ? manager.session(accountId)!.loadHistory(300).then((list) => list.map((i) => ({ ...i, deviceId: accountId })))
      : manager.loadMerged(300);
    history.then((list) => {
      if (alive) setItems((prev) => mergeFeed(list, prev, MAX_IN_MEMORY));
    });
    return () => {
      alive = false;
      off();
    };
  }, [manager, accountId, epoch]);

  // Preselect by tell partner only when the tab changes, so account updates don't undo a manual pick.
  const pickedForTab = useRef<string | undefined>(undefined);
  useEffect(() => {
    const key = tabKey(tab);
    const partner = key !== pickedForTab.current && tab.kind === "tell" ? tab.partner : undefined;
    pickedForTab.current = key;
    setSendAccount((c) => defaultSendAccount(items, accounts, c, partner));
  }, [tab, accounts]);

  // With no active account in the merged view, the first account's state shows why sending is blocked.
  const targetId = accountId ?? sendAccount ?? accounts[0].deviceId;
  const state = accounts.find((a) => a.deviceId === targetId)?.state ?? accounts[0].state;
  const activeAccounts = accounts.filter((a) => a.status === "active");

  useEffect(() => {
    document.querySelector(".tab.active")?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }, [tab]);

  const tabs = useMemo(() => {
    const channels = new Set<ChatChannel>();
    const partners = new Map<string, number>();
    for (const item of items) {
      if (item.channel === "tell") partners.set(tellPartner(item), item.ts);
      else channels.add(item.channel);
    }
    const list: Tab[] = [{ kind: "all" }];
    // Follow the plugin's channel order, channels no longer relayed go last.
    const order = [...state.relayChannels, ...ALL_CHANNELS.filter((c) => !state.relayChannels.includes(c))];
    for (const c of order) if (channels.has(c)) list.push({ kind: "channel", channel: c });
    const byRecent = [...partners.entries()].sort((a, b) => b[1] - a[1]);
    for (const [partner] of byRecent) list.push({ kind: "tell", partner });
    return list;
  }, [items, state.relayChannels]);

  const visible = useMemo(() => {
    if (tab.kind === "all") return items;
    if (tab.kind === "channel") return items.filter((i) => i.channel === tab.channel);
    return items.filter((i) => i.channel === "tell" && tellPartner(i) === tab.partner);
  }, [items, tab]);

  const visiblePending = pending.filter((p) => {
    if (merged ? !accounts.some((a) => a.deviceId === p.deviceId) : p.deviceId !== accountId) return false;
    if (tab.kind === "all") return true;
    if (tab.kind === "channel") return p.channel === tab.channel;
    return p.channel === "tell" && p.target === tab.partner;
  });

  function selectTab(next: Tab) {
    setTab(next);
    const key = tabKey(next);
    setUnread((prev) => ({ ...prev, [key]: 0 }));
  }

  const allTitle = merged ? "All accounts" : state.character ?? "Not logged in";
  const title = tab.kind === "all" ? allTitle : tab.kind === "channel" ? channelLabel(tab.channel) : tab.partner;

  return (
    <div class="chat">
      <header class="topbar">
        <div class="topbar-title">
          <h1>{title}</h1>
          {!merged && <StatusPill status={state.status} />}
        </div>
        <button class="icon-btn" aria-label="Settings" onClick={() => setSettingsOpen(true)}>
          <GearIcon />
        </button>
      </header>

      <nav class="tabs" aria-label="Conversations">
        {tabs.map((t) => {
          const key = tabKey(t);
          const color = t.kind === "channel" ? channelColor(t.channel) : t.kind === "tell" ? channelColor("tell") : "var(--accent)";
          const count = unread[key] ?? 0;
          return (
            <button
              key={key}
              class={`tab${key === activeKey.current ? " active" : ""}`}
              style={{ "--c": color }}
              aria-pressed={key === activeKey.current}
              onClick={() => selectTab(t)}
            >
              {t.kind === "all" ? "All" : t.kind === "channel" ? channelLabel(t.channel) : t.partner.split("@")[0]}
              {count > 0 && <span class="badge">{count > 99 ? "99+" : count}</span>}
            </button>
          );
        })}
      </nav>

      <MessageLog
        tabId={activeKey.current}
        items={visible}
        pending={visiblePending}
        showChannel={tab.kind === "all"}
        showAccount={merged && accounts.length > 1}
        accountLabel={(deviceId) => accounts.find((a) => a.deviceId === deviceId)?.label ?? ""}
        onRetry={(p) => void sends.retry(p)}
        onDismiss={sends.dismiss}
      />

      <Composer
        state={state}
        tab={tab}
        account={merged && activeAccounts.length > 1 && sendAccount ? {
          options: activeAccounts.map((a) => ({ deviceId: a.deviceId, label: a.label, online: a.state.status === "online" })),
          value: sendAccount,
          onChange: setSendAccount,
        } : undefined}
        onSend={(channel, text, target) => void sends.send(targetId, channel, text, target)}
      />

      {settingsOpen && (
        <SettingsView
          manager={manager}
          accounts={accounts}
          accountId={accountId}
          onClose={() => setSettingsOpen(false)}
          onCacheCleared={() => {
            setItems([]);
            setUnread({});
            setTab({ kind: "all" });
            setEpoch((e) => e + 1);
          }}
          onOpenAccount={onOpenAccount}
          onAddAccount={onAddAccount}
        />
      )}
    </div>
  );
}

function StatusPill({ status }: { status: SessionState["status"] }) {
  return <span class={`pill pill-${status}`}>{STATUS_LABELS[status]}</span>;
}

function MessageLog({ tabId, items, pending, showChannel, showAccount, accountLabel, onRetry, onDismiss }: {
  tabId: string;
  items: FeedItem[];
  pending: PendingSend[];
  showChannel: boolean;
  showAccount: boolean;
  accountLabel: (deviceId: string) => string;
  onRetry: (p: PendingSend) => void;
  onDismiss: (localId: number) => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const atBottom = useRef(true);
  const [showJump, setShowJump] = useState(false);

  function onScroll() {
    const el = ref.current!;
    atBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 48;
    if (atBottom.current) setShowJump(false);
  }

  function jump() {
    const el = ref.current!;
    el.scrollTop = el.scrollHeight;
    atBottom.current = true;
    setShowJump(false);
  }

  const lastId = useRef<string | undefined>(undefined);

  useLayoutEffect(() => {
    const last = items.at(-1);
    const newestId = last && feedKey(last);
    const grew = newestId !== lastId.current;
    lastId.current = newestId;
    if (atBottom.current) ref.current!.scrollTop = ref.current!.scrollHeight;
    else if (grew) setShowJump(true);
  }, [items, pending]);

  useLayoutEffect(() => {
    const last = items.at(-1);
    lastId.current = last && feedKey(last);
    jump();
  }, [tabId]);

  const rows = [];
  let prev: FeedItem | undefined;
  for (const item of items) {
    const key = feedKey(item);
    const day = dayLabel(item.ts);
    const newDay = !prev || dayLabel(prev.ts) !== day;
    if (newDay) rows.push(<div class="day" key={`day-${key}`}><span>{day}</span></div>);
    if (!showAccount && (!prev || prev.character !== item.character)) {
      rows.push(<div class="as-character" key={`char-${key}`}>as {item.character}</div>);
    }
    const continued = !!prev && !newDay && prev.deviceId === item.deviceId && prev.character === item.character &&
      prev.channel === item.channel && prev.sender === item.sender && prev.outgoing === item.outgoing && item.ts - prev.ts < GROUP_GAP_MS;
    rows.push(<Message key={key} item={item} continued={continued} showChannel={showChannel} showAccount={showAccount} />);
    prev = item;
  }

  return (
    <div class="log-wrap">
      <div class="log" ref={ref} onScroll={onScroll} role="log" aria-live="polite">
        {items.length === 0 && pending.length === 0 && (
          <div class="empty">
            <p>No messages yet.</p>
            <p class="hint">New chat from the game shows up here while the plugin is running.</p>
          </div>
        )}
        {rows}
        {pending.map((p) => (
          <div class={`msg out pending-msg${p.error ? " failed" : ""}`} style={{ "--c": channelColor(p.channel) }} key={`p-${p.localId}`}>
            <div class="msg-head">
              {showAccount && <span class="acct">{accountLabel(p.deviceId)}</span>}
              <span class="chip">{channelLabel(p.channel)}</span>
              {p.target && <span class="sender">to {p.target}</span>}
              <span class="time">{p.error ? "Not sent" : "Sending..."}</span>
            </div>
            <p class="msg-text">{p.text}</p>
            {p.error && (
              <div class="send-error">
                <span>{p.error}</span>
                <button class="link" onClick={() => onRetry(p)}>Retry</button>
                <button class="link" onClick={() => onDismiss(p.localId)}>Dismiss</button>
              </div>
            )}
          </div>
        ))}
      </div>
      {showJump && <button class="jump" onClick={jump}>New messages</button>}
    </div>
  );
}

function Message({ item, continued, showChannel, showAccount }: { item: ChatItem; continued: boolean; showChannel: boolean; showAccount: boolean }) {
  const isTell = item.channel === "tell";
  return (
    <div class={`msg${item.outgoing ? " out" : ""}${continued ? " cont" : ""}`} style={{ "--c": channelColor(item.channel) }}>
      {!continued && (
        <div class="msg-head">
          {showAccount && <span class="acct">{item.character}</span>}
          {showChannel && <span class="chip">{channelLabel(item.channel)}</span>}
          <span class="sender">
            {isTell && item.outgoing ? "to " : ""}
            {item.sender}
            {item.senderWorld && !item.sender.includes("@") && <span class="world">{item.senderWorld}</span>}
          </span>
          <time class="time" dateTime={new Date(item.ts).toISOString()}>{timeOfDay(item.ts)}</time>
        </div>
      )}
      <p class="msg-text">{item.text}</p>
    </div>
  );
}

function GearIcon() {
  return (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z" />
    </svg>
  );
}
