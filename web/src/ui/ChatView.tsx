import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "preact/hooks";
import { ALL_CHANNELS, type ChatChannel, type ChatItem } from "../core/protocol";
import type { Session, SessionState } from "../core/session";
import { Composer } from "./Composer";
import { SettingsView } from "./SettingsView";
import { channelColor, channelLabel, dayLabel, sendErrorText, STATUS_LABELS, tellPartner, timeOfDay } from "./format";

const MAX_IN_MEMORY = 3000;
const GROUP_GAP_MS = 5 * 60 * 1000;

export type Tab = { kind: "all" } | { kind: "channel"; channel: ChatChannel } | { kind: "tell"; partner: string };

export interface PendingSend {
  localId: number;
  channel: ChatChannel;
  target?: string;
  text: string;
  error?: string;
}

function tabKey(tab: Tab): string {
  if (tab.kind === "channel") return `ch:${tab.channel}`;
  if (tab.kind === "tell") return `tell:${tab.partner}`;
  return "all";
}

function itemTabKey(item: ChatItem): string {
  return item.channel === "tell" ? `tell:${tellPartner(item)}` : `ch:${item.channel}`;
}

function merge(existing: ChatItem[], incoming: ChatItem[]): ChatItem[] {
  const seen = new Set(existing.map((i) => i.id));
  const fresh = incoming.filter((i) => !seen.has(i.id));
  if (fresh.length === 0) return existing;
  const out = existing.concat(fresh).sort((a, b) => a.ts - b.ts);
  return out.length > MAX_IN_MEMORY ? out.slice(-MAX_IN_MEMORY) : out;
}

export function ChatView({ session, state, onUnpaired }: { session: Session; state: SessionState; onUnpaired: () => void }) {
  const [items, setItems] = useState<ChatItem[]>([]);
  const [tab, setTab] = useState<Tab>({ kind: "all" });
  const [unread, setUnread] = useState<Record<string, number>>({});
  const [pending, setPending] = useState<PendingSend[]>([]);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [epoch, setEpoch] = useState(0);
  const activeKey = useRef("all");
  activeKey.current = tabKey(tab);

  useEffect(() => {
    let alive = true;
    const off = session.onMessages((incoming) => {
      setItems((prev) => merge(prev, incoming));
      setUnread((prev) => {
        const next = { ...prev };
        for (const item of incoming) {
          const key = itemTabKey(item);
          if (!item.outgoing && key !== activeKey.current) next[key] = (next[key] ?? 0) + 1;
        }
        return next;
      });
    });
    session.loadHistory(300).then((history) => {
      if (alive) setItems((prev) => merge(history, prev));
    });
    return () => {
      alive = false;
      off();
    };
  }, [session, epoch]);

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
    for (const c of ALL_CHANNELS) if (channels.has(c)) list.push({ kind: "channel", channel: c });
    const byRecent = [...partners.entries()].sort((a, b) => b[1] - a[1]);
    for (const [partner] of byRecent) list.push({ kind: "tell", partner });
    return list;
  }, [items]);

  const visible = useMemo(() => {
    if (tab.kind === "all") return items;
    if (tab.kind === "channel") return items.filter((i) => i.channel === tab.channel);
    return items.filter((i) => i.channel === "tell" && tellPartner(i) === tab.partner);
  }, [items, tab]);

  const visiblePending = pending.filter((p) => {
    if (tab.kind === "all") return true;
    if (tab.kind === "channel") return p.channel === tab.channel;
    return p.channel === "tell" && p.target === tab.partner;
  });

  function selectTab(next: Tab) {
    setTab(next);
    const key = tabKey(next);
    setUnread((prev) => ({ ...prev, [key]: 0 }));
  }

  const nextLocalId = useRef(1);
  async function send(channel: ChatChannel, text: string, target?: string, retryOf?: number) {
    const localId = retryOf ?? nextLocalId.current++;
    const entry: PendingSend = { localId, channel, text, target };
    setPending((prev) => (retryOf ? prev.map((p) => (p.localId === retryOf ? entry : p)) : [...prev, entry]));
    let result;
    try {
      result = await session.send(channel, text, target);
    } catch {
      result = { ok: false, error: undefined };
    }
    if (result.ok) setPending((prev) => prev.filter((p) => p.localId !== localId));
    else setPending((prev) => prev.map((p) => (p.localId === localId ? { ...p, error: sendErrorText(result.error) } : p)));
  }

  function dismiss(localId: number) {
    setPending((prev) => prev.filter((p) => p.localId !== localId));
  }

  const title = tab.kind === "all" ? state.character ?? "ChatTerror" : tab.kind === "channel" ? channelLabel(tab.channel) : tab.partner;

  return (
    <div class="chat">
      <header class="topbar">
        <div class="topbar-title">
          <h1>{title}</h1>
          <StatusPill status={state.status} />
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

      <MessageLog tabId={activeKey.current} items={visible} pending={visiblePending} showChannel={tab.kind === "all"} onRetry={(p) => send(p.channel, p.text, p.target, p.localId)} onDismiss={dismiss} />

      <Composer state={state} tab={tab} onSend={(channel, text, target) => send(channel, text, target)} />

      {settingsOpen && (
        <SettingsView
          session={session}
          state={state}
          onClose={() => setSettingsOpen(false)}
          onCacheCleared={() => {
            setItems([]);
            setUnread({});
            setTab({ kind: "all" });
            setEpoch((e) => e + 1);
          }}
          onUnpaired={onUnpaired}
        />
      )}
    </div>
  );
}

function StatusPill({ status }: { status: SessionState["status"] }) {
  return <span class={`pill pill-${status}`}>{STATUS_LABELS[status]}</span>;
}

function MessageLog({ tabId, items, pending, showChannel, onRetry, onDismiss }: {
  tabId: string;
  items: ChatItem[];
  pending: PendingSend[];
  showChannel: boolean;
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
    const newestId = items.at(-1)?.id;
    const grew = newestId !== lastId.current;
    lastId.current = newestId;
    if (atBottom.current) ref.current!.scrollTop = ref.current!.scrollHeight;
    else if (grew) setShowJump(true);
  }, [items, pending]);

  useLayoutEffect(() => {
    lastId.current = items.at(-1)?.id;
    jump();
  }, [tabId]);

  const rows = [];
  let prev: ChatItem | undefined;
  for (const item of items) {
    const day = dayLabel(item.ts);
    const newDay = !prev || dayLabel(prev.ts) !== day;
    if (newDay) rows.push(<div class="day" key={`day-${item.id}`}><span>{day}</span></div>);
    if (!prev || prev.character !== item.character) {
      rows.push(<div class="as-character" key={`char-${item.id}`}>as {item.character}</div>);
    }
    const continued = !!prev && !newDay && prev.character === item.character && prev.channel === item.channel &&
      prev.sender === item.sender && prev.outgoing === item.outgoing && item.ts - prev.ts < GROUP_GAP_MS;
    rows.push(<Message key={item.id} item={item} continued={continued} showChannel={showChannel} />);
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

function Message({ item, continued, showChannel }: { item: ChatItem; continued: boolean; showChannel: boolean }) {
  const isTell = item.channel === "tell";
  return (
    <div class={`msg${item.outgoing ? " out" : ""}${continued ? " cont" : ""}`} style={{ "--c": channelColor(item.channel) }}>
      {!continued && (
        <div class="msg-head">
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
