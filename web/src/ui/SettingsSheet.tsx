import type { ComponentChildren } from "preact";
import { useEffect, useRef } from "preact/hooks";
import { BackIcon } from "./icons";

const FOCUSABLE = "button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [href], [tabindex]:not([tabindex='-1'])";

export function SettingsSheet({ title, onClose, children }: { title: string; onClose: () => void; children: ComponentChildren }) {
  const dialogRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialogRef.current?.focus();
    return () => previous?.focus();
  }, []);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    addEventListener("keydown", onKey);
    return () => removeEventListener("keydown", onKey);
  }, [onClose]);

  function trapTab(e: KeyboardEvent) {
    const dialog = dialogRef.current;
    if (e.key !== "Tab" || !dialog) return;
    const items = [...dialog.querySelectorAll<HTMLElement>(FOCUSABLE)];
    if (items.length === 0) return;
    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement;
    if (e.shiftKey && (active === first || active === dialog)) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && active === last) {
      e.preventDefault();
      first.focus();
    }
  }

  return (
    <section class="settings" ref={dialogRef} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby="settings-title" onKeyDown={trapTab}>
      <header class="settings-head">
        <button class="icon-btn" aria-label="Back" onClick={onClose}>
          <BackIcon />
        </button>
        <h2 id="settings-title">{title}</h2>
      </header>
      <div class="settings-body">{children}</div>
    </section>
  );
}

export function SettingsGroup({ label, note, children }: { label?: string; note?: ComponentChildren; children: ComponentChildren }) {
  return (
    <section class="settings-section">
      {label && <h3 class="settings-label">{label}</h3>}
      <div class="settings-group">{children}</div>
      {note}
    </section>
  );
}
