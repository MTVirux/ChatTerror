import { useEffect, useRef } from "preact/hooks";
import { customKey, deleteCustom, notifyChoice, setNotify, toggleMuted, togglePinned, type ChannelPrefs, type CustomChannel, type NotifyChoice } from "../core/channelPrefs";

const NOTIFY_OPTIONS: { value: NotifyChoice; label: string }[] = [
  { value: "default", label: "Default" },
  { value: "all", label: "All messages" },
  { value: "none", label: "Nothing" },
];

export type MenuTarget = { kind: "group"; custom: CustomChannel } | { kind: "dm"; key: string; partner: string };

export function ChannelMenu({ target, prefs, onChange, onEdit, onClose }: {
  target: MenuTarget;
  prefs: ChannelPrefs;
  onChange: (prefs: ChannelPrefs) => void;
  onEdit: (custom: CustomChannel) => void;
  onClose: () => void;
}) {
  const menuRef = useRef<HTMLDivElement>(null);
  const custom = target.kind === "group" ? target.custom : undefined;
  const key = target.kind === "group" ? customKey(target.custom) : target.key;
  const title = target.kind === "group" ? target.custom.name : target.partner.split("@")[0];
  const pinned = prefs.pinned.includes(key);
  const muted = prefs.muted.includes(key);
  const notify = notifyChoice(prefs, key);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    menuRef.current?.focus();
    return () => previous?.focus();
  }, []);

  // Capture on window so Escape closes only the menu, not the screen behind it.
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
        <h2 id="channel-menu-title" class="channel-menu-title">{title}</h2>
        <div class="settings-group">
          <button class="settings-row" onClick={() => choose(togglePinned(prefs, key))}>
            <span class="settings-text">{pinned ? "Unpin" : "Pin to top"}</span>
          </button>
          <button class="settings-row" onClick={() => choose(toggleMuted(prefs, key))}>
            <span class="settings-text">{muted ? "Unmute" : "Mute"}</span>
          </button>
          {custom && (
            <button class="settings-row" onClick={() => { onClose(); onEdit(custom); }}>
              <span class="settings-text">Edit group</span>
            </button>
          )}
          {custom && (
            <button class="settings-row danger" onClick={() => choose(deleteCustom(prefs, custom))}>
              <span class="settings-text">Delete group</span>
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
        {muted && <p class="settings-note">Muted, so this never notifies.</p>}
      </div>
    </div>
  );
}
