import { useEffect, useRef } from "preact/hooks";
import type { CharacterGroup } from "./characters";
import { CheckIcon } from "./icons";
import { initials, senderColor } from "./identity";
import type { Place } from "./place";
import { Portrait } from "./Portrait";

export function CharacterSheet({ groups, current, onPick, onPair, onClose }: {
  groups: CharacterGroup[];
  current: Place;
  onPick: (place: Place) => void;
  onPair: () => void;
  onClose: () => void;
}) {
  const sheetRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    sheetRef.current?.focus();
    return () => previous?.focus();
  }, []);

  // Capture on window so Escape closes only the sheet, not the drawer behind it.
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key !== "Escape") return;
      e.stopPropagation();
      onClose();
    }
    addEventListener("keydown", onKey, true);
    return () => removeEventListener("keydown", onKey, true);
  }, [onClose]);

  return (
    <div class="channel-menu-backdrop" onClick={onClose} onTouchStart={(e) => e.stopPropagation()}>
      <div ref={sheetRef} class="channel-menu" role="dialog" aria-modal="true" aria-labelledby="switch-title" tabIndex={-1} onClick={(e) => e.stopPropagation()}>
        <h2 id="switch-title" class="channel-menu-title">Switch character</h2>
        {groups.map((group) => (
          <section key={group.deviceId} class="char-group">
            {groups.length > 1 && <h3 class="settings-label">{group.label}</h3>}
            <div class="settings-group">
              {group.options.map((o) => {
                const chosen = o.place.deviceId === current.deviceId && o.place.character === current.character;
                return (
                  <button
                    key={o.place.character ?? o.place.deviceId}
                    class={`settings-row char-row${o.unread ? " unread" : ""}`}
                    aria-current={chosen ? "true" : undefined}
                    onClick={() => onPick(o.place)}
                  >
                    <span class="dm-avatar" style={{ "--c": senderColor(o.name) }} aria-hidden="true">
                      {initials(o.name)}
                      <Portrait name={o.name} world={o.world} />
                    </span>
                    <span class="settings-text">
                      {o.name}
                      <small>{o.world ? `${o.world} · ${o.status}` : o.status}</small>
                    </span>
                    {o.tells > 0 && <span class="count-badge" aria-label={`${o.tells} unread tells`}>{o.tells > 99 ? "99+" : o.tells}</span>}
                    {chosen && <CheckIcon />}
                  </button>
                );
              })}
            </div>
          </section>
        ))}
        <div class="settings-group char-pair">
          <button class="settings-row accent" onClick={onPair}>
            <span class="settings-text">Pair another game client</span>
          </button>
        </div>
      </div>
    </div>
  );
}
