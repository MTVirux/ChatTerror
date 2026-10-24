import { describe, expect, it } from "vitest";
import { createBackStack } from "./backStack";

function setup() {
  const tasks: (() => void)[] = [];
  const entries: unknown[] = [null];
  let index = 0;
  let pushes = 0;
  const history = {
    get state() {
      return entries[index];
    },
    pushState(state: unknown) {
      entries.splice(index + 1, Infinity, state);
      index++;
      pushes++;
    },
    go(delta: number) {
      tasks.push(() => {
        index += delta;
        stack.onPopState();
      });
    },
  };
  const stack = createBackStack(history, (fn) => tasks.push(fn));
  const flush = () => {
    while (tasks.length) tasks.shift()!();
  };
  return {
    stack,
    flush,
    depth: () => index,
    pushes: () => pushes,
    // Returns false when back would leave the app.
    pressBack() {
      if (index === 0) return false;
      index--;
      stack.onPopState();
      flush();
      return true;
    },
  };
}

function layer(t: ReturnType<typeof setup>) {
  const l = { open: true, close: () => {} };
  const off = t.stack.open(() => {
    l.open = false;
    off();
  });
  l.close = () => {
    l.open = false;
    off();
  };
  return l;
}

describe("back stack", () => {
  it("closes an open layer on back instead of leaving the app", () => {
    const t = setup();
    const menu = layer(t);
    t.flush();
    expect(t.pressBack()).toBe(true);
    expect(menu.open).toBe(false);
    expect(t.depth()).toBe(0);
  });

  it("drops the entry when a layer closes some other way", () => {
    const t = setup();
    const menu = layer(t);
    t.flush();
    menu.close();
    t.flush();
    expect(t.depth()).toBe(0);
  });

  it("closes only the top layer per back press", () => {
    const t = setup();
    const drawer = layer(t);
    t.flush();
    const menu = layer(t);
    t.flush();
    expect(t.depth()).toBe(2);
    t.pressBack();
    expect(menu.open).toBe(false);
    expect(drawer.open).toBe(true);
    t.pressBack();
    expect(drawer.open).toBe(false);
    expect(t.depth()).toBe(0);
  });

  it("keeps one entry when one layer replaces another in the same update", () => {
    const t = setup();
    const drawer = layer(t);
    t.flush();
    drawer.close();
    const settings = layer(t);
    t.flush();
    expect(t.depth()).toBe(1);
    expect(t.pushes()).toBe(1);
    t.pressBack();
    expect(settings.open).toBe(false);
    expect(t.depth()).toBe(0);
  });

  it("doesn't add entries after back closes a layer above another", () => {
    const t = setup();
    layer(t);
    t.flush();
    layer(t);
    t.flush();
    t.pressBack();
    expect(t.pushes()).toBe(2);
    expect(t.depth()).toBe(1);
  });
});
