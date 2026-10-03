import { Fragment } from "preact";
import { customKey, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import { customColor, isTells } from "./channels";
import { ChatBubbleIcon, PlusIcon } from "./icons";
import { initials } from "./identity";
import { useLongPress } from "./longPress";

export function ChannelRail({ rail, selected, prefs, unreadOf, onSelect, onMenu, onAdd }: {
  rail: CustomChannel[];
  selected: string | null;
  prefs: ChannelPrefs;
  unreadOf: (custom: CustomChannel) => { count: number; tells: number };
  onSelect: (custom: CustomChannel) => void;
  onMenu: (custom: CustomChannel) => void;
  onAdd: () => void;
}) {
  return (
    <nav class="rail" aria-label="Channels">
      {rail.map((custom) => (
        <Fragment key={custom.id}>
          <RailChannel
            custom={custom}
            selected={custom.id === selected}
            muted={prefs.muted.includes(customKey(custom))}
            unread={unreadOf(custom)}
            onSelect={() => onSelect(custom)}
            onMenu={() => onMenu(custom)}
          />
          {isTells(custom) && <div class="rail-sep" aria-hidden="true" />}
        </Fragment>
      ))}
      {rail.length > 0 && (
        <div class="rail-item">
          <button class="rail-icon add" aria-label="New channel" title="New channel" onClick={onAdd}>
            <PlusIcon size={24} />
          </button>
        </div>
      )}
    </nav>
  );
}

function RailChannel({ custom, selected, muted, unread, onSelect, onMenu }: {
  custom: CustomChannel;
  selected: boolean;
  muted: boolean;
  unread: { count: number; tells: number };
  onSelect: () => void;
  onMenu: () => void;
}) {
  const press = useLongPress(onMenu, onSelect);
  const mark = selected ? " selected" : unread.count > 0 ? " unread" : "";
  return (
    <div class="rail-item">
      {mark && <span class={`rail-mark${mark}`} aria-hidden="true" />}
      <button
        class={`rail-icon channel-icon${selected ? " selected" : ""}${muted ? " muted" : ""}`}
        style={{ "--c": customColor(custom) }}
        aria-label={unread.tells > 0 ? `${custom.name}, ${unread.tells} unread tells` : custom.name}
        aria-current={selected ? "page" : undefined}
        title={custom.name}
        {...press}
      >
        {isTells(custom) ? <ChatBubbleIcon /> : initials(custom.name)}
      </button>
      {unread.tells > 0 && <span class="rail-badge" aria-hidden="true">{unread.tells > 99 ? "99+" : unread.tells}</span>}
    </div>
  );
}
