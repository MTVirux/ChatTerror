import { useEffect, useRef } from "preact/hooks";
import { deleteCustom, moveChannel, notifyChoice, setNotify, toggleMuted, togglePinned, type ChannelPrefs, type CustomChannel, type NotifyChoice } from "../core/channelPrefs";
import { channelKey, type ChannelRef } from "./channels";
import { channelColor } from "./format";

const NOTIFY_OPTIONS: { value: NotifyChoice; label: string }[] = [
  { value: "default", label: "Default" },
  { value: "all", label: "All messages" },
  { value: "none", label: "Nothing" },
];

export function customColor(custom: CustomChannel | undefined): string {
  const first = custom?.channels.find((c) => c !== "tell") ?? custom?.channels[0];
  return first ? channelColor(first) : "var(--muted)";
}

export function channelTitle(ref: ChannelRef, custom: CustomChannel | undefined): string {
  return ref.kind === "custom" ? `#${custom?.name ?? ""}` : ref.partner.split("@")[0];
}

export function ChannelMenu({ channel, custom, keys, prefs, onChange, onEdit, onClose }: {
  channel: ChannelRef;
  custom?: CustomChannel;
  // The category's rows in display order, for moving this one.
  keys: string[];
  prefs: ChannelPrefs;
  onChange: (prefs: ChannelPrefs) => void;
  onEdit?: () => void;
  onClose: () => void;
}) {
  const menuRef = useRef<HTMLDivElement>(null);
  const key = channelKey(channel);
  const pinned = prefs.pinned.includes(key);
  const muted = prefs.muted.includes(key);
  const notify = notifyChoice(prefs, key);
  const index = keys.indexOf(key);
  // Pinned rows always lead, so a row only moves past others with the same pin state.
  const canMove = (step: -1 | 1) => {
    const neighbor = keys[index + step];
    return index >= 0 && neighbor !== undefined && prefs.pinned.includes(neighbor) === pinned;
  };

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    menuRef.current?.focus();
    return () => previous?.focus();
  }, []);

  // Capture on window so Escape closes only the menu, not the drawer behind it.
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key !== "Escape") return;
      e.stopPropagation();
      onClose();
    }
    addEventListener("keydown", onKey, true);
    return () => removeEventListener("keydown", onKey, true);
  }, [onClose]);

  function choose(next: ChannelPrefs) {
    onChange(next);
    onClose();
  }

  return (
    <div class="channel-menu-backdrop" onClick={onClose} onTouchStart={(e) => e.stopPropagation()}>
      <div
        ref={menuRef}
        class="channel-menu"
        role="dialog"
        aria-modal="true"
        aria-labelledby="channel-menu-title"
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id="channel-menu-title" class="channel-menu-title">{channelTitle(channel, custom)}</h2>
        <div class="settings-group">
          <button class="settings-row" onClick={() => choose(togglePinned(prefs, key))}>
            <span class="settings-text">{pinned ? "Unpin" : "Pin to top"}</span>
          </button>
          {canMove(-1) && (
            <button class="settings-row" onClick={() => choose(moveChannel(prefs, channel.character, keys, key, -1))}>
              <span class="settings-text">Move up</span>
            </button>
          )}
          {canMove(1) && (
            <button class="settings-row" onClick={() => choose(moveChannel(prefs, channel.character, keys, key, 1))}>
              <span class="settings-text">Move down</span>
            </button>
          )}
          <button class="settings-row" onClick={() => choose(toggleMuted(prefs, key))}>
            <span class="settings-text">{muted ? "Unmute" : "Mute"}</span>
          </button>
          {custom && onEdit && (
            <button class="settings-row" onClick={() => { onClose(); onEdit(); }}>
              <span class="settings-text">Edit channel</span>
            </button>
          )}
          {custom && (
            <button class="settings-row danger" onClick={() => choose(deleteCustom(prefs, custom))}>
              <span class="settings-text">Delete channel</span>
            </button>
          )}
        </div>
        <h3 class="settings-label channel-menu-label">Notifications</h3>
        <div class="settings-group" role="radiogroup" aria-label="Notifications">
          {NOTIFY_OPTIONS.map((o) => (
            <button key={o.value} class="settings-row" role="radio" aria-checked={notify === o.value} onClick={() => choose(setNotify(prefs, key, o.value))}>
              <span class="settings-text">{o.label}</span>
              <span class="radio" aria-hidden="true" />
            </button>
          ))}
        </div>
        {custom?.channels.includes("tell") ? (
          <p class="settings-note">Tells still notify by each player's own setting.</p>
        ) : (
          muted && <p class="settings-note">Muted, so this channel never notifies.</p>
        )}
      </div>
    </div>
  );
}
