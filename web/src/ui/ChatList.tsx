import type { ComponentChildren } from "preact";
import { useEffect, useRef, useState } from "preact/hooks";
import { useBackClose } from "./backStack";
import { customColor } from "./channels";
import { previewText, type ChatRow } from "./chatRows";
import { listTime } from "./format";
import { MoreIcon, MutedIcon, PinIcon, PlusIcon } from "./icons";
import { initials, senderColor } from "./identity";
import { useLongPress } from "./longPress";
import { Portrait } from "./Portrait";

export function ChatList({ me, otherTells, rows, selected, canStart, body, unreadOf, onOpen, onRowMenu, onSwitch, onSettings, onPair, onNew }: {
  me: { name: string; world?: string; status: string; online: boolean };
  otherTells: number;
  rows: ChatRow[];
  selected: string | null;
  // False with no character, or a pending or removed client. Hides the + button.
  canStart: boolean;
  // Shown instead of the rows.
  body?: ComponentChildren;
  unreadOf: (row: ChatRow) => number;
  onOpen: (row: ChatRow) => void;
  onRowMenu: (row: ChatRow) => void;
  onSwitch: () => void;
  onSettings: () => void;
  onPair: () => void;
  onNew: () => void;
}) {
  const detail = me.world ? `${me.world} · ${me.status}` : me.status;
  const elsewhere = otherTells > 0 ? `, ${otherTells} unread tells on other characters` : "";
  return (
    <div class="chat-list">
      <header class="list-head">
        <button class="list-me" aria-haspopup="dialog" aria-label={`${me.name}, ${detail}${elsewhere}. Switch character`} onClick={onSwitch}>
          <span class="me-avatar-wrap" aria-hidden="true">
            <span class="me-avatar" style={{ "--c": senderColor(me.name) }}>
              {initials(me.name)}
              <Portrait name={me.name} world={me.world} />
            </span>
            {me.online && <span class="me-dot" />}
            {otherTells > 0 && <span class="me-badge">{otherTells > 99 ? "99+" : otherTells}</span>}
          </span>
          <span class="list-me-text">
            <span class="list-me-name">{me.name}</span>
            <small>{detail}</small>
          </span>
        </button>
        <MoreMenu onSettings={onSettings} onPair={onPair} />
      </header>
      <div class="list-body">
        {body ?? (rows.length > 0
          ? rows.map((row) => (
            <Row key={row.key} row={row} selected={row.key === selected} count={unreadOf(row)} onOpen={() => onOpen(row)} onMenu={() => onRowMenu(row)} />
          ))
          : <p class="hint list-empty">No chats yet. Tap + to start one.</p>)}
      </div>
      {canStart && (
        <button class="list-fab" aria-label="New chat" onClick={onNew}>
          <PlusIcon size={26} />
        </button>
      )}
    </div>
  );
}

function MoreMenu({ onSettings, onPair }: { onSettings: () => void; onPair: () => void }) {
  const [open, setOpen] = useState(false);
  const toggle = useRef<HTMLButtonElement>(null);
  const first = useRef<HTMLButtonElement>(null);
  useBackClose(() => setOpen(false), open);

  // Capture on window so Escape closes only this menu, not the screen behind it.
  useEffect(() => {
    if (!open) return;
    first.current?.focus();
    function onKey(e: KeyboardEvent) {
      if (e.key !== "Escape") return;
      e.stopPropagation();
      setOpen(false);
      toggle.current?.focus();
    }
    addEventListener("keydown", onKey, true);
    return () => removeEventListener("keydown", onKey, true);
  }, [open]);

  function pick(action: () => void) {
    setOpen(false);
    action();
  }

  return (
    <div class="list-more">
      <button ref={toggle} class="icon-btn" aria-label="More options" aria-expanded={open} onClick={() => setOpen(!open)}>
        <MoreIcon />
      </button>
      {open && (
        <>
          <div class="list-menu-backdrop" onClick={() => setOpen(false)} />
          <div class="list-menu">
            <button ref={first} onClick={() => pick(onSettings)}>Settings</button>
            <button onClick={() => pick(onPair)}>Pair another game client</button>
          </div>
        </>
      )}
    </div>
  );
}

function Row({ row, selected, count, onOpen, onMenu }: {
  row: ChatRow;
  selected: boolean;
  count: number;
  onOpen: () => void;
  onMenu: () => void;
}) {
  const press = useLongPress(onMenu, onOpen);
  const unread = count > 0;
  return (
    <button
      class={`chat-row${selected ? " selected" : ""}${unread ? " unread" : ""}${row.muted ? " muted" : ""}`}
      aria-current={selected ? "page" : undefined}
      {...press}
    >
      {row.kind === "dm" ? (
        <span class="chat-avatar" style={{ "--c": senderColor(row.name) }} aria-hidden="true">
          {initials(row.name)}
          <Portrait name={row.name} world={row.world} />
        </span>
      ) : (
        <span class="chat-avatar group" style={{ "--c": customColor(row.custom) }} aria-hidden="true">
          {initials(row.name)}
        </span>
      )}
      <span class="chat-row-body">
        <span class="chat-row-line">
          <span class="chat-row-name">{row.name}</span>
          <span class="chat-row-time">{row.last ? listTime(row.last.ts) : ""}</span>
        </span>
        <span class="chat-row-line">
          <span class="chat-row-preview">{previewText(row)}</span>
          {row.pinned && <PinIcon />}
          {row.muted && <MutedIcon />}
          {unread && <span class="chat-row-badge" aria-label={`${count} unread`}>{count > 99 ? "99+" : count}</span>}
        </span>
      </span>
    </button>
  );
}
