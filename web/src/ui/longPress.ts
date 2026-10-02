import { useEffect, useRef } from "preact/hooks";

export const LONG_PRESS_MS = 500;
export const LONG_PRESS_SLOP = 10;

export interface Timers {
  set(fn: () => void, ms: number): unknown;
  clear(handle: unknown): void;
}

const realTimers: Timers = {
  set: (fn, ms) => setTimeout(fn, ms),
  clear: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export function createLongPress(onLongPress: () => void, timers: Timers = realTimers) {
  let timer: unknown = null;
  let start: { x: number; y: number } | null = null;
  let fired = false;

  function cancel() {
    if (timer !== null) timers.clear(timer);
    timer = null;
    start = null;
  }

  return {
    down(x: number, y: number) {
      cancel();
      fired = false;
      start = { x, y };
      timer = timers.set(() => {
        timer = null;
        start = null;
        fired = true;
        onLongPress();
      }, LONG_PRESS_MS);
    },
    move(x: number, y: number) {
      if (start && Math.hypot(x - start.x, y - start.y) > LONG_PRESS_SLOP) cancel();
    },
    cancel,
    // A touch long press can also raise contextmenu; the click after it must still be swallowed.
    contextMenu() {
      if (start) fired = true;
      cancel();
      onLongPress();
    },
    // True when this click ends a long press and should not count.
    takeClick(): boolean {
      const was = fired;
      fired = false;
      return was;
    },
  };
}

export function useLongPress(onLongPress: () => void, onClick: () => void) {
  const callback = useRef(onLongPress);
  callback.current = onLongPress;
  const press = useRef<ReturnType<typeof createLongPress> | null>(null);
  press.current ??= createLongPress(() => callback.current());
  const p = press.current;
  useEffect(() => p.cancel, [p]);

  return {
    onPointerDown(e: PointerEvent) {
      if (e.isPrimary && e.button === 0) p.down(e.clientX, e.clientY);
    },
    onPointerMove: (e: PointerEvent) => p.move(e.clientX, e.clientY),
    onPointerUp: p.cancel,
    onPointerCancel: p.cancel,
    onPointerLeave: p.cancel,
    onContextMenu(e: MouseEvent) {
      e.preventDefault();
      p.contextMenu();
    },
    onClick() {
      if (!p.takeClick()) onClick();
    },
  };
}
