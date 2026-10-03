import { useLayoutEffect, useMemo, useRef, useState } from "preact/hooks";
import type { AccountView, FeedItem } from "../core/accounts";
import type { ChatChannel } from "../core/protocol";
import { feedKey } from "./feed";
import { channelColor, channelLabel, timeOfDay } from "./format";
import { initials, senderColor } from "./identity";
import { buildRows, incomingAfter, type Row } from "./messages";
import type { PendingSend } from "./pending";

export function MessageList({ viewId, items, unreadCount, showAccount, pending, accountOf, onRetry, onDismiss }: {
  viewId: string;
  items: FeedItem[];
  unreadCount: number;
  showAccount: boolean;
  pending: PendingSend[];
  accountOf: (deviceId: string) => AccountView | undefined;
  onRetry: (p: PendingSend) => void;
  onDismiss: (localId: number) => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const atBottom = useRef(true);
  const [showJump, setShowJump] = useState(false);

  // Messages arriving while the view is open stay below the NEW divider.
  const anchor = useMemo(() => {
    const last = items.at(-1);
    return last && feedKey(last);
  }, [viewId, unreadCount]);
  const marked = unreadCount > 0 ? unreadCount + incomingAfter(items, anchor) : 0;
  const rows = useMemo(() => buildRows(items, marked, showAccount), [items, marked, showAccount]);

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
  }, [viewId]);

  return (
    <div class="log-wrap">
      <div class="log" ref={ref} onScroll={onScroll} role="log" aria-live="polite">
        {items.length === 0 && pending.length === 0 && (
          <div class="empty">
            <p>No messages yet.</p>
            <p class="hint">New chat from the game shows up here while the plugin is running.</p>
          </div>
        )}
        {rows.map((row) => <MessageRow key={feedKey(row.item)} row={row} account={showAccount ? accountOf(row.item.deviceId) : undefined} showAccount={showAccount} />)}
        {pending.map((p) => {
          const account = accountOf(p.deviceId);
          return (
            <PendingRow
              key={`p-${p.localId}`}
              send={p}
              character={account?.character ?? account?.label ?? ""}
              account={showAccount ? account : undefined}
              onRetry={onRetry}
              onDismiss={onDismiss}
            />
          );
        })}
      </div>
      {showJump && <button class="jump" onClick={jump}>New messages</button>}
    </div>
  );
}

// The account comes from which pairing delivered the row, never from what the plugin put in it.
function Tags({ account, channel }: { account?: AccountView; channel: ChatChannel }) {
  return (
    <>
      {account && <span class="acct" style={{ "--c": account.color }} title={account.label}>{account.label}</span>}
      <span class="chip" style={{ "--c": channelColor(channel) }}>{channelLabel(channel)}</span>
    </>
  );
}

function Avatar({ name }: { name: string }) {
  return <span class="avatar" style={{ "--c": senderColor(name) }} aria-hidden="true">{initials(name)}</span>;
}

function MessageRow({ row, account, showAccount }: { row: Row; account?: AccountView; showAccount: boolean }) {
  const { item } = row;
  const time = <time class="time" dateTime={new Date(item.ts).toISOString()}>{timeOfDay(item.ts)}</time>;
  // System lines like echo, errors or sales have no sender.
  const [name, ownWorld] = (item.sender || channelLabel(item.channel)).split("@");
  const world = ownWorld ?? item.senderWorld;
  return (
    <>
      {row.day && <div class="day"><span>{row.day}</span></div>}
      {row.newMarker && <div class="new-divider" role="separator" aria-label="New messages"><span>NEW</span></div>}
      {row.head ? (
        <div class="msg head">
          {/* Outgoing tells carry the target as sender, so own rows use the character. */}
          <Avatar name={item.outgoing ? item.character : name} />
          <div class="msg-body">
            <div class="msg-head">
              <span class={`sender${item.outgoing ? " own" : ""}`}>
                {item.channel === "tell" && item.outgoing ? `to ${name}` : name}
                {world && !showAccount && <span class="world">{world}</span>}
              </span>
              {showAccount && <Tags account={account} channel={item.channel} />}
              {time}
            </div>
            <p class="msg-text">{item.text}</p>
          </div>
        </div>
      ) : (
        <div class="msg cont">
          <p class="msg-text">{item.text}</p>
        </div>
      )}
    </>
  );
}

function PendingRow({ send, character, account, onRetry, onDismiss }: {
  send: PendingSend;
  character: string;
  account?: AccountView;
  onRetry: (p: PendingSend) => void;
  onDismiss: (localId: number) => void;
}) {
  return (
    <div class={`msg head pending-msg${send.error ? " failed" : ""}`}>
      <Avatar name={character} />
      <div class="msg-body">
        <div class="msg-head">
          <span class="sender own">{send.target ? `to ${send.target.split("@")[0]}` : character}</span>
          {account && <Tags account={account} channel={send.channel} />}
          <span class="time">{send.error ? "Not sent" : "Sending..."}</span>
        </div>
        <p class="msg-text">{send.text}</p>
        {send.error && (
          <div class="send-error">
            <span>{send.error}</span>
            <button class="link" onClick={() => onRetry(send)}>Retry</button>
            <button class="link" onClick={() => onDismiss(send.localId)}>Dismiss</button>
          </div>
        )}
      </div>
    </div>
  );
}
