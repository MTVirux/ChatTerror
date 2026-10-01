import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { connectRelay, RESUME_RECONNECT_MS } from "./relay";

class FakeSocket {
  static OPEN = 1;
  static all: FakeSocket[] = [];
  readyState = 0;
  sent: string[] = [];
  onopen: (() => void) | null = null;
  onmessage: ((e: { data: unknown }) => void) | null = null;
  onclose: (() => void) | null = null;

  constructor(public url: string) {
    FakeSocket.all.push(this);
  }

  send(data: string) {
    this.sent.push(data);
  }

  close() {
    this.readyState = 3;
    queueMicrotask(() => this.onclose?.());
  }

  open() {
    this.readyState = FakeSocket.OPEN;
    this.onopen?.();
  }
}

const docListeners = new Map<string, () => void>();
let visibilityState = "visible";

function setVisibility(state: string) {
  visibilityState = state;
  docListeners.get("visibilitychange")?.();
}

beforeEach(() => {
  vi.useFakeTimers();
  FakeSocket.all = [];
  docListeners.clear();
  visibilityState = "visible";
  vi.stubGlobal("WebSocket", FakeSocket);
  vi.stubGlobal("document", {
    get visibilityState() {
      return visibilityState;
    },
    addEventListener: (type: string, cb: () => void) => docListeners.set(type, cb),
    removeEventListener: (type: string) => docListeners.delete(type),
  });
  vi.stubGlobal("window", { addEventListener: () => undefined, removeEventListener: () => undefined });
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("connectRelay", () => {
  it("reopens a socket that still looks open after a long time in the background", async () => {
    const onClose = vi.fn();
    connectRelay("tok", { onFrame: () => undefined, onClose }, "ws://x/ws");
    FakeSocket.all[0].open();

    setVisibility("hidden");
    vi.advanceTimersByTime(RESUME_RECONNECT_MS);
    setVisibility("visible");
    await Promise.resolve();

    expect(onClose).toHaveBeenCalledTimes(1);
    expect(FakeSocket.all).toHaveLength(2);
    expect(FakeSocket.all[0].readyState).toBe(3);
  });

  it("keeps the socket after a short time in the background", () => {
    const onClose = vi.fn();
    connectRelay("tok", { onFrame: () => undefined, onClose }, "ws://x/ws");
    FakeSocket.all[0].open();

    setVisibility("hidden");
    vi.advanceTimersByTime(RESUME_RECONNECT_MS - 1);
    setVisibility("visible");

    expect(onClose).not.toHaveBeenCalled();
    expect(FakeSocket.all).toHaveLength(1);
  });
});
