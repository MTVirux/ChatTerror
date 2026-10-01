import type { AccountView } from "../core/accounts";
import type { Selection } from "./selection";

export function AccountStrip({ accounts, selected, onSelect }: { accounts: AccountView[]; selected: Selection; onSelect: (s: Selection) => void }) {
  const totalUnread = accounts.reduce((n, a) => n + a.unread, 0);
  return (
    <nav class="accounts" aria-label="Accounts">
      <AccountTab label="All" active={selected === "all"} unread={selected === "all" ? 0 : totalUnread} onClick={() => onSelect("all")} />
      {accounts.map((a) => (
        <AccountTab key={a.deviceId} label={a.label} active={selected === a.deviceId} unread={a.unread} muted={a.status === "revoked"} onClick={() => onSelect(a.deviceId)} />
      ))}
      <button class={`account-tab add${selected === "add" ? " active" : ""}`} aria-label="Add account" aria-pressed={selected === "add"} onClick={() => onSelect("add")}>+</button>
    </nav>
  );
}

function AccountTab({ label, active, unread, muted, onClick }: { label: string; active: boolean; unread: number; muted?: boolean; onClick: () => void }) {
  return (
    <button class={`account-tab${active ? " active" : ""}${muted ? " muted" : ""}`} aria-pressed={active} onClick={onClick}>
      {label}
      {unread > 0 && <span class="badge">{unread > 99 ? "99+" : unread}</span>}
    </button>
  );
}
