import { GearIcon, SwapIcon } from "./icons";
import { initials, senderColor } from "./identity";
import { Portrait } from "./Portrait";

export function CharacterBar({ name, world, status, online, otherTells, onSwitch, onSettings }: {
  name: string;
  world?: string;
  status: string;
  online: boolean;
  otherTells: number;
  onSwitch: () => void;
  onSettings: () => void;
}) {
  const detail = world ? `${world} · ${status}` : status;
  const elsewhere = otherTells > 0 ? `, ${otherTells} unread tells on other characters` : "";
  return (
    <div class="me-bar">
      <button class="me" aria-haspopup="dialog" aria-label={`${name}, ${detail}${elsewhere}. Switch character`} onClick={onSwitch}>
        <span class="me-avatar-wrap" aria-hidden="true">
          <span class="me-avatar" style={{ "--c": senderColor(name) }}>
            {initials(name)}
            <Portrait name={name} world={world} />
          </span>
          {online && <span class="me-dot" />}
          {otherTells > 0 && <span class="me-badge">{otherTells > 99 ? "99+" : otherTells}</span>}
        </span>
        <span class="me-text">
          <span class="me-name">{name}</span>
          <small>{detail}</small>
        </span>
        <SwapIcon />
      </button>
      <button class="icon-btn" aria-label="App settings" onClick={onSettings}>
        <GearIcon />
      </button>
    </div>
  );
}
