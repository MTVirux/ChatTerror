import { useEffect, useRef, useState } from "preact/hooks";
import type { CustomChannel } from "../core/channelPrefs";
import { ALL_CHANNELS, type ChatChannel } from "../core/protocol";
import { useBackClose } from "./backStack";
import { customColor } from "./channels";
import { channelColor, channelLabel } from "./format";
import { CloseIcon } from "./icons";
import { initials } from "./identity";
import { SettingsGroup, SettingsSheet } from "./SettingsSheet";

function newId(): string {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
}

// Relayed channels first in plugin order, then the rest.
export function pickerChannels(relayChannels: ChatChannel[]): ChatChannel[] {
  const all = [...relayChannels, ...ALL_CHANNELS.filter((c) => !relayChannels.includes(c))];
  return all.filter((c) => c !== "tell");
}

export function defaultName(channels: ChatChannel[]): string {
  return channels.map(channelLabel).join(", ");
}

export function GroupEditor({ character, custom, relayChannels, onSave, onClose }: {
  character: string;
  custom?: CustomChannel;
  relayChannels: ChatChannel[];
  onSave: (custom: CustomChannel) => void;
  onClose: () => void;
}) {
  const [step, setStep] = useState<1 | 2>(1);
  const [picked, setPicked] = useState<ChatChannel[]>(custom?.channels.filter((c) => c !== "tell") ?? []);
  const [name, setName] = useState(custom?.name ?? "");
  const [id] = useState(() => custom?.id ?? newId());
  const nameRef = useRef<HTMLInputElement>(null);
  const nextRef = useRef<HTMLButtonElement>(null);
  const opened = useRef(false);

  const options = pickerChannels(relayChannels);
  const ordered = options.filter((c) => picked.includes(c));
  const group: CustomChannel = { id, character, name: name.trim() || defaultName(ordered), channels: ordered };
  const back = step === 2 ? () => setStep(1) : onClose;

  // Capture on window so Escape steps back here only, not on the page behind it too.
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key !== "Escape") return;
      e.stopPropagation();
      back();
    }
    addEventListener("keydown", onKey, true);
    return () => removeEventListener("keydown", onKey, true);
  }, [step, onClose]);

  useBackClose(() => setStep(1), step === 2);

  // Changing step removes the focused control, so focus moves into the new step. The sheet focuses itself on open.
  useEffect(() => {
    if (opened.current) (step === 2 ? nameRef : nextRef).current?.focus();
    opened.current = true;
  }, [step]);

  function toggle(channel: ChatChannel) {
    setPicked((list) => (list.includes(channel) ? list.filter((c) => c !== channel) : [...list, channel]));
  }

  function save(event: Event) {
    event.preventDefault();
    if (ordered.length === 0) return;
    onSave(group);
    onClose();
  }

  const title = custom ? "Edit group" : "New group";

  if (step === 1) {
    const pickRow = (c: ChatChannel) => (
      <label key={c} class={`settings-row pick-row${relayChannels.includes(c) ? "" : " dim"}`}>
        <span class="hash" style={{ "--c": channelColor(c) }} aria-hidden="true">#</span>
        <span class="settings-text">{channelLabel(c)}</span>
        <input type="checkbox" class="pick-check" checked={picked.includes(c)} onChange={() => toggle(c)} />
      </label>
    );
    const relayed = options.filter((c) => relayChannels.includes(c));
    const rest = options.filter((c) => !relayChannels.includes(c));
    return (
      <SettingsSheet title={title} onClose={back}>
        <p class="page-sub">Pick channels</p>
        {picked.length > 0 && (
          <div class="pick-chips">
            {picked.map((c) => (
              <button key={c} type="button" class="pick-chip" style={{ "--c": channelColor(c) }} aria-label={`Remove ${channelLabel(c)}`} onClick={() => toggle(c)}>
                {channelLabel(c)}
                <CloseIcon />
              </button>
            ))}
          </div>
        )}
        {relayed.length > 0 && <SettingsGroup label="Relayed by the plugin">{relayed.map(pickRow)}</SettingsGroup>}
        {rest.length > 0 && <SettingsGroup label="Not relayed">{rest.map(pickRow)}</SettingsGroup>}
        <div class="page-action">
          <button ref={nextRef} type="button" class="btn primary" disabled={picked.length === 0} onClick={() => setStep(2)}>Next</button>
        </div>
      </SettingsSheet>
    );
  }

  return (
    <SettingsSheet title={title} onClose={back}>
      <form onSubmit={save}>
        <SettingsGroup>
          <div class="settings-row name-row">
            <span class="group-avatar" style={{ "--c": customColor(group) }} aria-hidden="true">{initials(group.name)}</span>
            <input
              ref={nameRef}
              value={name}
              onInput={(e) => setName(e.currentTarget.value)}
              placeholder={defaultName(ordered)}
              aria-label="Group name"
              maxLength={40}
            />
          </div>
        </SettingsGroup>
        <SettingsGroup label="Channels">
          {ordered.map((c) => (
            <div key={c} class="settings-row">
              <span class="hash" style={{ "--c": channelColor(c) }} aria-hidden="true">#</span>
              <span class="settings-text">{channelLabel(c)}</span>
            </div>
          ))}
        </SettingsGroup>
        <div class="page-action">
          <button type="submit" class="btn primary">{custom ? "Save" : "Create"}</button>
        </div>
      </form>
    </SettingsSheet>
  );
}
