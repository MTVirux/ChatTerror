import { describe, expect, it } from "vitest";
import { createLongPress, LONG_PRESS_MS, type Timers } from "./longPress";

function fakeTimers() {
  let pending: { fn: () => void; ms: number } | null = null;
  const timers: Timers = {
    set(fn, ms) {
      pending = { fn, ms };
      return pending;
    },
    clear(handle) {
      if (handle === pending) pending = null;
    },
  };
  return {
    timers,
    elapse() {
      const p = pending;
      pending = null;
      p?.fn();
      return p?.ms;
    },
  };
}

function setup() {
  const t = fakeTimers();
  let presses = 0;
  const press = createLongPress(() => presses++, t.timers);
  return { press, t, presses: () => presses };
}

describe("long press", () => {
  it("fires after the hold and swallows the click that follows", () => {
    const { press, t, presses } = setup();
    press.down(0, 0);
    expect(t.elapse()).toBe(LONG_PRESS_MS);
    expect(presses()).toBe(1);
    press.cancel();
    expect(press.takeClick()).toBe(true);
    expect(press.takeClick()).toBe(false);
  });

  it("lets a quick tap through", () => {
    const { press, t, presses } = setup();
    press.down(0, 0);
    press.cancel();
    t.elapse();
    expect(presses()).toBe(0);
    expect(press.takeClick()).toBe(false);
  });

  it("cancels when the pointer moves too far but not on a small wobble", () => {
    const { press, t, presses } = setup();
    press.down(0, 0);
    press.move(4, 5);
    press.move(12, 0);
    t.elapse();
    expect(presses()).toBe(0);

    press.down(0, 0);
    press.move(6, 6);
    t.elapse();
    expect(presses()).toBe(1);
  });

  it("opens on contextmenu and only swallows the click when it came from a press", () => {
    const { press, presses } = setup();
    press.contextMenu();
    expect(presses()).toBe(1);
    expect(press.takeClick()).toBe(false);

    press.down(0, 0);
    press.contextMenu();
    expect(presses()).toBe(2);
    expect(press.takeClick()).toBe(true);
  });

  it("resets a stale long press on the next press", () => {
    const { press, t } = setup();
    press.down(0, 0);
    t.elapse();
    press.down(0, 0);
    press.cancel();
    expect(press.takeClick()).toBe(false);
  });
});
