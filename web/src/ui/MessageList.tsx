import { useLayoutEffect, useMemo, useRef, useState } from "preact/hooks";
import type { FeedItem } from "../core/accounts";
import { feedKey } from "./feed";
import type { ChatChannel } from "../core/protocol";
import { channelColor, channelLabel, timeOfDay } from "./format";
import { initials, senderColor } from "./identity";
import { bubbleSide, buildRows, incomingAfter, pendingHeads, type Row } from "./messages";
import type { PendingSend } from "./pending";
import { Portrait } from "./Portrait";

export function MessageList({ viewId, items, unreadCount, chatTags, showNames, pending, onRetry, onDismiss }: {
  viewId: string;
  items: FeedItem[];
  unreadCount: number;
  chatTags: boolean;
  showNames: boolean;
  pending: PendingSend[];
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
  const rows = useMemo(() => buildRows(items, marked), [items, marked]);
  const pendingHead = pendingHeads(items, pending, Date.now());

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
        {rows.map((row) => <MessageRow key={feedKey(row.item)} row={row} chatTag={chatTags} showName={showNames} />)}
        {pending.map((p, i) => (
          <PendingRow
            key={`p-${p.localId}`}
            send={p}
            head={pendingHead[i]}
            chatTag={chatTags}
            onRetry={onRetry}
            onDismiss={onDismiss}
          />
        ))}
      </div>
      {showJump && <button class="jump" onClick={jump}>New messages</button>}
    </div>
  );
}

function Avatar({ name, world }: { name: string; world?: string }) {
  return (
    <span class="avatar" style={{ "--c": senderColor(name) }} aria-hidden="true">
      {initials(name)}
      <Portrait name={name} world={world} />
    </span>
  );
}

function ChatTag({ channel }: { channel: ChatChannel }) {
  return <span class="chat-tag" style={{ "--c": channelColor(channel) }}>{channelLabel(channel)}</span>;
}

function MessageRow({ row, chatTag, showName }: { row: Row; chatTag: boolean; showName: boolean }) {
  const { item } = row;
  const side = bubbleSide(item);
  const tag = chatTag && <ChatTag channel={item.channel} />;
  const time = <time class="time" dateTime={new Date(item.ts).toISOString()}>{timeOfDay(item.ts)}</time>;
  const [name, nameWorld] = item.sender.split("@");
  const world = nameWorld ?? item.senderWorld;
  return (
    <>
      {row.day && <div class="day"><span>{row.day}</span></div>}
      {row.newMarker && <div class="new-divider" role="separator" aria-label="New messages"><span>NEW</span></div>}
      {side === "system" && <div class="system-line">{tag}{item.text}{time}</div>}
      {side === "out" && (
        <div class={`bubble-row out${row.head ? " head" : ""}`}>
          <div class="bubble"><span class="sr-only">You: </span>{tag}{item.text}{time}</div>
        </div>
      )}
      {side === "in" && (
        <div class={`bubble-row in${row.head ? " head" : ""}`}>
          {row.head ? <Avatar name={name} world={world} /> : <span class="avatar-gap" />}
          <div class="bubble">
            {row.head && showName ? (
              <span class="bubble-name" style={{ "--c": senderColor(name) }}>
                {name}
                {world && <span class="world">{world}</span>}
              </span>
            ) : (
              <span class="sr-only">{name}: </span>
            )}
            {tag}{item.text}{time}
          </div>
        </div>
      )}
    </>
  );
}

function PendingRow({ send, head, chatTag, onRetry, onDismiss }: {
  send: PendingSend;
  head: boolean;
  chatTag: boolean;
  onRetry: (p: PendingSend) => void;
  onDismiss: (localId: number) => void;
}) {
  return (
    <>
      <div class={`bubble-row out pending${head ? " head" : ""}${send.error ? " failed" : ""}`}>
        <div class="bubble">
          <span class="sr-only">You: </span>
          {chatTag && <ChatTag channel={send.channel} />}
          {send.text}
          <span class="time">{send.error ? "Not sent" : "Sending..."}</span>
        </div>
      </div>
      {send.error && (
        <div class="send-error">
          <span>{send.error}</span>
          <button class="link" onClick={() => onRetry(send)}>Retry</button>
          <button class="link" onClick={() => onDismiss(send.localId)}>Dismiss</button>
        </div>
      )}
    </>
  );
}
