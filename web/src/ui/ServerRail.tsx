import type { ComponentChildren } from "preact";
import type { AccountView } from "../core/accounts";
import { initials } from "./identity";
import type { Server } from "./nav";
import type { UnreadSummary, UnreadTracker } from "./unread";

export function ServerRail({ accounts, server, unread, onSelect }: {
  accounts: AccountView[];
  server: Server;
  unread: UnreadTracker;
  onSelect: (server: Server) => void;
}) {
  return (
    <nav class="rail" aria-label="Servers">
      {accounts.length > 1 && (
        <>
          <RailItem label="Home" selected={server === "home"} summary={unread.total(accounts.map((a) => a.deviceId))} onClick={() => onSelect("home")}>
            <HomeIcon />
          </RailItem>
          <div class="rail-sep" aria-hidden="true" />
        </>
      )}
      {accounts.map((a) => (
        <RailItem
          key={a.deviceId}
          label={a.label}
          selected={server === a.deviceId}
          summary={unread.summary(a.deviceId)}
          color={a.color}
          revoked={a.status === "revoked"}
          pending={a.state.status === "pending"}
          onClick={() => onSelect(a.deviceId)}
        >
          {initials(a.label)}
        </RailItem>
      ))}
      <RailItem label="Add a server" add selected={server === "add"} onClick={() => onSelect("add")}>
        <PlusIcon />
      </RailItem>
    </nav>
  );
}

function RailItem({ label, selected, summary, color, revoked, pending, add, onClick, children }: {
  label: string;
  selected: boolean;
  summary?: UnreadSummary;
  color?: string;
  revoked?: boolean;
  pending?: boolean;
  add?: boolean;
  onClick: () => void;
  children: ComponentChildren;
}) {
  const mark = selected ? " selected" : summary?.unread ? " unread" : "";
  const kind = add ? " add" : color ? "" : " home";
  return (
    <div class="rail-item">
      {mark && <span class={`rail-mark${mark}`} aria-hidden="true" />}
      <button
        class={`rail-icon${kind}${selected ? " selected" : ""}${revoked ? " revoked" : ""}`}
        style={color ? { "--c": color } : undefined}
        aria-label={label}
        aria-current={selected ? "page" : undefined}
        title={label}
        onClick={onClick}
      >
        {children}
      </button>
      {summary && summary.tells > 0 && <span class="rail-badge">{summary.tells > 99 ? "99+" : summary.tells}</span>}
      {pending && <span class="rail-badge clock" aria-label="Waiting for approval"><ClockIcon /></span>}
    </div>
  );
}

function HomeIcon() {
  return (
    <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path d="M21 12a8 8 0 0 1-11.6 7.1L4 20l1-4.6A8 8 0 1 1 21 12z" />
    </svg>
  );
}

function PlusIcon() {
  return (
    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true">
      <path d="M12 5v14M5 12h14" />
    </svg>
  );
}

function ClockIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </svg>
  );
}
