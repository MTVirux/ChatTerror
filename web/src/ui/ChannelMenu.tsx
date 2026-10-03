import { useEffect, useRef } from "preact/hooks";
import { customKey, deleteCustom, moveChannel, notifyChoice, setNotify, toggleMuted, togglePinned, type ChannelPrefs, type CustomChannel, type NotifyChoice } from "../core/channelPrefs";
import { isTells } from "./channels";

const NOTIFY_OPTIONS: { value: NotifyChoice; label: string }[] = [
  { value: "default", label: "Default" },
  { value: "all", label: "All messages" },
  { value: "none", label: "Nothing" },
];

// keys are the rail's custom keys in order, Tells first.
export type MenuTarget = { kind: "channel"; custom: CustomChannel; keys: string[] } | { kind: "partner"; key: string; partner: string };

// Tells stays first, so nothing moves into or out of the first place.
export function canMoveChannel(keys: string[], key: string, step: -1 | 1): boolean {
  const from = keys.indexOf(key);
  const to = from + step;
  return from > 0 && to > 0 && to < keys.length;
}

export function ChannelMenu({ target, prefs, onChange, onEdit, onClose }: {
  target: MenuTarget;
  prefs: ChannelPrefs;
  onChange: (prefs: ChannelPrefs) => void;
  onEdit: (custom: CustomChannel) => void;
  onClose: () => void;
}) {
  const menuRef = useRef<HTMLDivElement>(null);
  const custom = target.kind === "channel" ? target.custom : undefined;
  const key = target.kind === "channel" ? customKey(target.custom) : target.key;
  const title = target.kind === "channel" ? target.custom.name : target.partner.split("@")[0];
  const pinned = prefs.pinned.includes(key);
  const muted = prefs.muted.includes(key);
  const notify = notifyChoice(prefs, key);
  // The plugin matches tells per partner, so a tells-only channel has nothing to set.
  const notifiable = !custom || custom.channels.some((c) => c !== "tell");

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

  function move(step: -1 | 1) {
    if (target.kind === "channel") choose(moveChannel(prefs, target.custom.character, target.keys, key, step));
  }

  const canMove = (step: -1 | 1) => target.kind === "channel" && canMoveChannel(target.keys, key, step);

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
        <h2 id="channel-menu-title" class="channel-menu-title">{title}</h2>
        <div class="settings-group">
          {target.kind === "partner" && (
            <button class="settings-row" onClick={() => choose(togglePinned(prefs, key))}>
              <span class="settings-text">{pinned ? "Unpin" : "Pin to top"}</span>
            </button>
          )}
          {canMove(-1) && (
            <button class="settings-row" onClick={() => move(-1)}>
              <span class="settings-text">Move up</span>
            </button>
          )}
          {canMove(1) && (
            <button class="settings-row" onClick={() => move(1)}>
              <span class="settings-text">Move down</span>
            </button>
          )}
          <button class="settings-row" onClick={() => choose(toggleMuted(prefs, key))}>
            <span class="settings-text">{muted ? "Unmute" : "Mute"}</span>
          </button>
          {custom && !isTells(custom) && (
            <button class="settings-row" onClick={() => { onClose(); onEdit(custom); }}>
              <span class="settings-text">Edit channel</span>
            </button>
          )}
          {custom && !isTells(custom) && (
            <button class="settings-row danger" onClick={() => choose(deleteCustom(prefs, custom))}>
              <span class="settings-text">Delete channel</span>
            </button>
          )}
        </div>
        {notifiable && (
          <>
            <h3 class="settings-label channel-menu-label">Notifications</h3>
            <div class="settings-group" role="radiogroup" aria-label="Notifications">
              {NOTIFY_OPTIONS.map((o) => (
                <button key={o.value} class="settings-row" role="radio" aria-checked={notify === o.value} onClick={() => choose(setNotify(prefs, key, o.value))}>
                  <span class="settings-text">{o.label}</span>
                  <span class="radio" aria-hidden="true" />
                </button>
              ))}
            </div>
          </>
        )}
        {custom?.channels.includes("tell") ? (
          <p class="settings-note">Tells still notify by each player's own setting.</p>
        ) : (
          muted && <p class="settings-note">Muted, so this never notifies.</p>
        )}
      </div>
    </div>
  );
}
