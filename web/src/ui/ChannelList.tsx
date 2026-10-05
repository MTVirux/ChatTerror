import type { ChannelPrefs, CustomChannel } from "../core/channelPrefs";
import { customColor, isTells, type SubRow } from "./channels";
import { channelColor } from "./format";
import { ChatBubbleIcon } from "./icons";
import { initials, senderColor } from "./identity";
import { useLongPress } from "./longPress";
import { Portrait } from "./Portrait";

export function ChannelList({ custom, character, rows, selected, prefs, unreadOf, onOpen, onMenu, onRowMenu }: {
  custom: CustomChannel;
  character: string;
  rows: SubRow[];
  selected: string | null;
  prefs: ChannelPrefs;
  unreadOf: (row: SubRow) => number;
  onOpen: (row: SubRow) => void;
  onMenu: () => void;
  onRowMenu: (row: SubRow) => void;
}) {
  return (
    <div class="sidebar-body">
      <button class="sidebar-head" aria-label={`${custom.name} options`} onClick={onMenu}>
        <span class="sidebar-title">{custom.name}</span>
        <small class="sidebar-status">{character}</small>
      </button>
      <div class="channels">
        {rows.map((row) => {
          const shared = { row, selected: row.key === selected, muted: prefs.muted.includes(row.key), count: unreadOf(row), onOpen: () => onOpen(row) };
          return row.kind === "partner"
            ? <PartnerRow key={row.key} {...shared} onMenu={() => onRowMenu(row)} />
            : <ViewRow key={row.key} {...shared} color={row.channel ? channelColor(row.channel) : customColor(custom)} />;
        })}
        {isTells(custom) && rows.length === 1 && <p class="hint channels-empty">No tells yet. Start one from All tells.</p>}
      </div>
    </div>
  );
}

function rowClass(selected: boolean, count: number, muted: boolean): string {
  return `channel${selected ? " selected" : ""}${count > 0 ? " unread" : ""}${muted ? " muted" : ""}`;
}

function ViewRow({ row, color, selected, muted, count, onOpen }: {
  row: SubRow;
  color: string;
  selected: boolean;
  muted: boolean;
  count: number;
  onOpen: () => void;
}) {
  return (
    <button class={rowClass(selected, count, muted)} style={{ "--c": color }} aria-current={selected ? "page" : undefined} onClick={onOpen}>
      <span class="hash" aria-hidden="true">{row.hash ? "#" : <ChatBubbleIcon size={18} />}</span>
      <span class="channel-name">{row.label}</span>
    </button>
  );
}

function PartnerRow({ row, selected, muted, count, onOpen, onMenu }: {
  row: SubRow;
  selected: boolean;
  muted: boolean;
  count: number;
  onOpen: () => void;
  onMenu: () => void;
}) {
  const press = useLongPress(onMenu, onOpen);
  const [name, world] = (row.partner ?? row.label).split("@");
  return (
    <button class={rowClass(selected, count, muted)} aria-current={selected ? "page" : undefined} {...press}>
      <span class="dm-avatar" style={{ "--c": senderColor(name) }} aria-hidden="true">
        {initials(name)}
        <Portrait name={name} world={world} />
      </span>
      <span class="channel-name">
        {name}
        {world && <span class="world">{world}</span>}
      </span>
      {count > 0 && <span class="count-badge" aria-label={`${count} unread`}>{count > 99 ? "99+" : count}</span>}
    </button>
  );
}
