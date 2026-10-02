import type { ComponentChildren } from "preact";
import { useEffect, useRef } from "preact/hooks";
import { swipeResult } from "./swipe";

export function Drawer({ open, onOpenChange, drawer, children }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  drawer: ComponentChildren;
  children: ComponentChildren;
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  const opener = useRef<HTMLElement | null>(null);
  const touch = useRef<{ x: number; y: number } | null>(null);

  useEffect(() => {
    if (open) {
      opener.current = document.activeElement as HTMLElement | null;
      panelRef.current?.focus();
      return;
    }
    // Leave focus alone when something else (like a settings sheet) already took it.
    const active = document.activeElement;
    if (!active || active === document.body || panelRef.current?.contains(active)) opener.current?.focus();
    opener.current = null;
  }, [open]);

  useEffect(() => {
    if (!open) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onOpenChange(false);
    }
    addEventListener("keydown", onKey);
    return () => removeEventListener("keydown", onKey);
  }, [open, onOpenChange]);

  function onTouchStart(e: TouchEvent) {
    const t = e.touches[0];
    touch.current = e.touches.length === 1 ? { x: t.clientX, y: t.clientY } : null;
  }

  function onTouchEnd(e: TouchEvent) {
    const start = touch.current;
    touch.current = null;
    const t = e.changedTouches[0];
    if (!start || !t) return;
    const result = swipeResult({ startX: start.x, dx: t.clientX - start.x, dy: t.clientY - start.y, open });
    if (result) onOpenChange(result === "open");
  }

  return (
    <div class="shell" onTouchStart={onTouchStart} onTouchEnd={onTouchEnd}>
      <div class="shell-main" inert={open}>{children}</div>
      <div class={`drawer-backdrop${open ? " open" : ""}`} aria-hidden="true" onClick={() => onOpenChange(false)} />
      <div
        ref={panelRef}
        class={`drawer${open ? " open" : ""}`}
        role={open ? "dialog" : undefined}
        aria-modal={open ? "true" : undefined}
        aria-label="Navigation"
        tabIndex={-1}
        inert={!open}
      >
        {drawer}
      </div>
    </div>
  );
}
