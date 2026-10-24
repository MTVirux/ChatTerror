import { useEffect, useRef, useState } from "preact/hooks";
import { TELL_TARGET } from "./format";
import { ChatBubbleIcon } from "./icons";
import { initials, senderColor } from "./identity";
import type { Person } from "./newChat";
import { Portrait } from "./Portrait";
import { SettingsGroup, SettingsSheet } from "./SettingsSheet";

export function NewChat({ character, friends, recent, onOpenDm, onNewGroup, onClose }: {
  character: string;
  friends: Person[];
  recent: Person[];
  onOpenDm: (partner: string) => void;
  onNewGroup: () => void;
  onClose: () => void;
}) {
  const [typing, setTyping] = useState(false);
  const [target, setTarget] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  const trimmed = target.trim();
  const valid = TELL_TARGET.test(trimmed);

  useEffect(() => {
    if (typing) inputRef.current?.focus();
  }, [typing]);

  function open(event: Event) {
    event.preventDefault();
    if (valid) onOpenDm(trimmed);
  }

  return (
    <SettingsSheet title="New chat" onClose={onClose}>
      <p class="page-sub">as {character}</p>
      <SettingsGroup note={typing && trimmed !== "" && !valid && <p class="settings-note error">Use the full name and world, like Y'shtola Rhul@Twintania</p>}>
        <button class="settings-row" onClick={onNewGroup}>
          <span class="new-chat-icon" aria-hidden="true">#</span>
          <span class="settings-text">New group</span>
        </button>
        <button class="settings-row" aria-expanded={typing} onClick={() => setTyping(!typing)}>
          <span class="new-chat-icon" aria-hidden="true"><ChatBubbleIcon size={20} /></span>
          <span class="settings-text">Tell someone new</span>
        </button>
        {typing && (
          <form class="settings-row name-row" onSubmit={open}>
            <input
              ref={inputRef}
              value={target}
              onInput={(e) => setTarget(e.currentTarget.value)}
              placeholder="First Last@World"
              aria-label="Name and world"
              autocapitalize="words"
              spellcheck={false}
            />
            <button type="submit" class="btn primary small" disabled={!valid}>Open</button>
          </form>
        )}
      </SettingsGroup>
      <People label="ChatTerror friends" people={friends} onOpen={onOpenDm} />
      <People label="Recent tells" people={recent} onOpen={onOpenDm} />
    </SettingsSheet>
  );
}

function People({ label, people, onOpen }: { label: string; people: Person[]; onOpen: (partner: string) => void }) {
  if (people.length === 0) return null;
  return (
    <SettingsGroup label={label}>
      {people.map((p) => (
        <button key={p.partner} class="settings-row person-row" onClick={() => onOpen(p.partner)}>
          <span class="dm-avatar" style={{ "--c": senderColor(p.name) }} aria-hidden="true">
            {initials(p.name)}
            <Portrait name={p.name} world={p.world} />
          </span>
          <span class="settings-text">
            {p.name}
            {p.world && <small>{p.world}</small>}
          </span>
        </button>
      ))}
    </SettingsGroup>
  );
}
