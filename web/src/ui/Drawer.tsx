import type { ComponentChildren } from "preact";
import { useEffect, useRef, useState } from "preact/hooks";
import { ignoresSwipe, swipeResult } from "./swipe";

const DOCKED_QUERY = "(min-width: 900px)";

function useMedia(query: string): boolean {
  const [matches, setMatches] = useState(() => matchMedia(query).matches);
  useEffect(() => {
    const list = matchMedia(query);
    const onChange = () => setMatches(list.matches);
    onChange();
    list.addEventListener("change", onChange);
    return () => list.removeEventListener("change", onChange);
  }, [query]);
  return matches;
}

export function Drawer({ open: requested, onOpenChange, swipe = true, narrow = false, drawer, children }: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  swipe?: boolean;
  narrow?: boolean;
  drawer: ComponentChildren;
  children: ComponentChildren;
}) {
  // Wide screens keep the drawer beside the chat, so it never opens over it.
  const docked = useMedia(DOCKED_QUERY);
  const open = requested && !docked;
  const panelRef = useRef<HTMLDivElement>(null);
  const mainRef = useRef<HTMLDivElement>(null);
  const touch = useRef<{ x: number; y: number } | null>(null);
  const swiped = useRef(false);
  const fromButton = useRef(false);

  useEffect(() => {
    if (docked && requested) onOpenChange(false);
  }, [docked, requested]);

  useEffect(() => {
    if (open) {
      fromButton.current = !swiped.current;
      swiped.current = false;
      panelRef.current?.focus();
      return;
    }
    // Only a menu button open gives focus back, so a swipe never pops the keyboard. Leave focus alone when a settings sheet took it.
    const active = document.activeElement;
    if (fromButton.current && (!active || active === document.body || panelRef.current?.contains(active))) {
      mainRef.current?.querySelector<HTMLElement>(".menu-btn")?.focus();
    }
    fromButton.current = false;
  }, [open]);

  useEffect(() => {
    if (!open) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onOpenChange(false);
    }
    addEventListener("keydown", onKey);
    return () => removeEventListener("keydown", onKey);
  }, [open, onOpenChange]);

  // An open drawer owns one history entry so the back gesture closes it.
  useEffect(() => {
    if (!open) return;
    // Per-open token, so an entry left over from a reload or a forward step is never mistaken for ours.
    const token = Math.random();
    history.pushState({ drawer: token }, "");
    const onPop = () => onOpenChange(false);
    addEventListener("popstate", onPop);
    return () => {
      removeEventListener("popstate", onPop);
      if (history.state?.drawer === token) history.back();
    };
  }, [open]);

  function onTouchStart(e: TouchEvent) {
    const t = e.touches[0];
    const fromField = e.target instanceof Element && ignoresSwipe(e.target.tagName);
    touch.current = swipe && !docked && e.touches.length === 1 && !fromField ? { x: t.clientX, y: t.clientY } : null;
  }

  function onTouchEnd(e: TouchEvent) {
    const start = touch.current;
    touch.current = null;
    const t = e.changedTouches[0];
    if (!start || !t) return;
    const result = swipeResult({ dx: t.clientX - start.x, dy: t.clientY - start.y, open });
    if (!result) return;
    if (result === "open") swiped.current = true;
    onOpenChange(result === "open");
  }

  return (
    <div class={`shell${docked ? " docked" : ""}`} onTouchStart={onTouchStart} onTouchEnd={onTouchEnd}>
      <div ref={mainRef} class="shell-main" inert={open}>{children}</div>
      <div class={`drawer-backdrop${open ? " open" : ""}`} aria-hidden="true" onClick={() => onOpenChange(false)} />
      <div
        ref={panelRef}
        class={`drawer${open ? " open" : ""}${narrow ? " narrow" : ""}`}
        role={open ? "dialog" : undefined}
        aria-modal={open ? "true" : undefined}
        aria-label="Navigation"
        tabIndex={-1}
        inert={!open && !docked}
      >
        {drawer}
      </div>
    </div>
  );
}
