import { useLayoutEffect, useRef } from "preact/hooks";

export type HistoryLike = {
  readonly state: unknown;
  pushState(state: unknown, unused: string): void;
  go(delta: number): void;
};

const DEPTH_KEY = "chatterrorLayers";

// Each open layer (drawer, sheet, menu) owns one history entry, so the back button closes the top one instead of leaving the app.
export function createBackStack(history: HistoryLike, defer: (fn: () => void) => void) {
  const layers: (() => void)[] = [];
  let traversing = false;
  let scheduled = false;

  const depth = () => {
    const value = (history.state as Record<string, unknown> | null)?.[DEPTH_KEY];
    return typeof value === "number" ? value : 0;
  };

  function sync() {
    scheduled = false;
    if (traversing) return;
    const current = depth();
    for (let d = current + 1; d <= layers.length; d++) history.pushState({ [DEPTH_KEY]: d }, "");
    if (layers.length < current) {
      traversing = true;
      history.go(layers.length - current);
    }
  }

  // Deferred so a layer closing and another opening in the same update keep the entry instead of going back and forth.
  function schedule() {
    if (scheduled) return;
    scheduled = true;
    defer(sync);
  }

  return {
    open(close: () => void): () => void {
      layers.push(close);
      schedule();
      return () => {
        const i = layers.indexOf(close);
        if (i >= 0) layers.splice(i, 1);
        schedule();
      };
    },
    onPopState() {
      if (traversing) traversing = false;
      else while (layers.length > depth()) layers.pop()!();
      schedule();
    },
  };
}

let shared: ReturnType<typeof createBackStack> | null = null;

function sharedStack() {
  if (!shared) {
    shared = createBackStack(history, (fn) => setTimeout(fn, 0));
    addEventListener("popstate", shared.onPopState);
  }
  return shared;
}

// Layout effect so a layer is registered before the deferred sync runs, even when its update came from a back press.
export function useBackClose(onClose: () => void, active: boolean) {
  const latest = useRef(onClose);
  latest.current = onClose;
  useLayoutEffect(() => (active ? sharedStack().open(() => latest.current()) : undefined), [active]);
}
