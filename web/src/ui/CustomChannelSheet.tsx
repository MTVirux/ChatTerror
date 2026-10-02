import { useEffect, useRef, useState } from "preact/hooks";
import type { CustomChannel } from "../core/channelPrefs";
import { ALL_CHANNELS, type ChatChannel } from "../core/protocol";
import { channelColor, channelLabel } from "./format";

function newId(): string {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
}

// Relayed channels first in plugin order, then the rest.
export function pickerChannels(relayChannels: ChatChannel[]): ChatChannel[] {
  return [...relayChannels, ...ALL_CHANNELS.filter((c) => !relayChannels.includes(c))];
}

export function defaultName(channels: ChatChannel[]): string {
  return channels.map((c) => (c === "tell" ? "Tells" : channelLabel(c))).join(", ");
}

export function CustomChannelSheet({ character, custom, relayChannels, onSave, onClose }: {
  character: string;
  custom?: CustomChannel;
  relayChannels: ChatChannel[];
  onSave: (custom: CustomChannel) => void;
  onClose: () => void;
}) {
  const [name, setName] = useState(custom?.name ?? "");
  const [channels, setChannels] = useState<ChatChannel[]>(custom?.channels ?? []);
  const sheetRef = useRef<HTMLDivElement>(null);
  const options = pickerChannels(relayChannels);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    sheetRef.current?.querySelector("input")?.focus();
    return () => previous?.focus();
  }, []);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key !== "Escape") return;
      e.stopPropagation();
      onClose();
    }
    addEventListener("keydown", onKey, true);
    return () => removeEventListener("keydown", onKey, true);
  }, [onClose]);

  function toggle(channel: ChatChannel) {
    setChannels((list) => (list.includes(channel) ? list.filter((c) => c !== channel) : [...list, channel]));
  }

  function submit(event: Event) {
    event.preventDefault();
    if (channels.length === 0) return;
    const ordered = options.filter((c) => channels.includes(c));
    onSave({ id: custom?.id ?? newId(), character, name: name.trim() || defaultName(ordered), channels: ordered });
    onClose();
  }

  return (
    <div class="channel-menu-backdrop" onClick={onClose} onTouchStart={(e) => e.stopPropagation()}>
      <div ref={sheetRef} class="channel-menu" role="dialog" aria-modal="true" aria-labelledby="custom-channel-title" onClick={(e) => e.stopPropagation()}>
        <form onSubmit={submit}>
          <h2 id="custom-channel-title" class="channel-menu-title">{custom ? "Edit chat channel" : `New chat channel for ${character}`}</h2>
          <div class="settings-group">
            <label class="settings-row">
              <input
                class="custom-name"
                aria-label="Channel name"
                placeholder={channels.length > 0 ? defaultName(options.filter((c) => channels.includes(c))) : "Channel name"}
                value={name}
                maxLength={40}
                onInput={(e) => setName(e.currentTarget.value)}
              />
            </label>
          </div>
          <h3 class="settings-label channel-menu-label">Shows messages from</h3>
          <div class="settings-group">
            {options.map((c) => (
              <label key={c} class="settings-row">
                <span class="hash" style={{ "--c": channelColor(c) }} aria-hidden="true">#</span>
                <span class="settings-text">
                  {c === "tell" ? "Tells" : channelLabel(c)}
                  {!relayChannels.includes(c) && <small>Not relayed by the plugin</small>}
                </span>
                <input type="checkbox" class="switch" checked={channels.includes(c)} onChange={() => toggle(c)} />
              </label>
            ))}
          </div>
          <div class="custom-actions">
            <button type="button" class="btn ghost" onClick={onClose}>Cancel</button>
            <button type="submit" class="btn primary" disabled={channels.length === 0}>{custom ? "Save" : "Create"}</button>
          </div>
        </form>
      </div>
    </div>
  );
}
