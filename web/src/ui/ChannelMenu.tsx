import { useEffect, useRef } from "preact/hooks";
import { notifyChoice, setNotify, toggleMuted, togglePinned, type ChannelPrefs, type NotifyChoice } from "../core/channelPrefs";
import { channelKey, channelSlug, type ChannelRef } from "./channels";

const NOTIFY_OPTIONS: { value: NotifyChoice; label: string }[] = [
  { value: "default", label: "Default" },
  { value: "all", label: "All messages" },
  { value: "none", label: "Nothing" },
];

export function channelTitle(ref: ChannelRef): string {
  return ref.kind === "chat" ? `#${channelSlug(ref.channel)}` : ref.partner.split("@")[0];
}

export function ChannelMenu({ channel, prefs, onChange, onClose }: {
  channel: ChannelRef;
  prefs: ChannelPrefs;
  onChange: (prefs: ChannelPrefs) => void;
  onClose: () => void;
}) {
  const menuRef = useRef<HTMLDivElement>(null);
  const key = channelKey(channel);
  const pinned = prefs.pinned.includes(key);
  const muted = prefs.muted.includes(key);
  const notify = notifyChoice(prefs, key);

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
        <h2 id="channel-menu-title" class="channel-menu-title">{channelTitle(channel)}</h2>
        <div class="settings-group">
          <button class="settings-row" onClick={() => choose(togglePinned(prefs, key))}>
            <span class="settings-text">{pinned ? "Unpin" : "Pin to top"}</span>
          </button>
          <button class="settings-row" onClick={() => choose(toggleMuted(prefs, key))}>
            <span class="settings-text">{muted ? "Unmute" : "Mute"}</span>
          </button>
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
        {muted && <p class="settings-note">Muted, so this channel never notifies.</p>}
      </div>
    </div>
  );
}
