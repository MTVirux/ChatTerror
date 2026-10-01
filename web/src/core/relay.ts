import { parseServerFrame, type ClientFrame, type ServerFrame } from "./protocol";

export interface RelayHandlers {
  onFrame(frame: ServerFrame): void;
  onClose(): void;
}

export interface RelayConnection {
  send(frame: ClientFrame): boolean;
  close(): void;
}

const MIN_DELAY_MS = 1000;
const MAX_DELAY_MS = 30000;

function defaultUrl(): string {
  return `${location.protocol === "https:" ? "wss:" : "ws:"}//${location.host}/ws`;
}

export function connectRelay(token: string, handlers: RelayHandlers, url = defaultUrl()): RelayConnection {
  let socket: WebSocket | null = null;
  let attempt = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let stopped = false;

  function open() {
    clearTimeout(timer);
    timer = undefined;
    const ws = new WebSocket(url);
    socket = ws;
    ws.onopen = () => ws.send(JSON.stringify({ t: "auth", token }));
    ws.onmessage = (event) => {
      if (typeof event.data !== "string") return;
      const frame = parseServerFrame(event.data);
      if (!frame) return;
      if (frame.t === "authOk") attempt = 0;
      handlers.onFrame(frame);
    };
    ws.onclose = () => {
      if (socket !== ws) return;
      socket = null;
      if (stopped) return;
      handlers.onClose();
      const delay = Math.min(MAX_DELAY_MS, MIN_DELAY_MS * 2 ** attempt);
      attempt++;
      timer = setTimeout(open, delay);
    };
  }

  function reconnectNow() {
    if (stopped || socket) return;
    attempt = 0;
    open();
  }

  function onVisibility() {
    if (document.visibilityState === "visible") reconnectNow();
  }

  document.addEventListener("visibilitychange", onVisibility);
  window.addEventListener("online", reconnectNow);
  open();

  return {
    send(frame) {
      if (socket?.readyState !== WebSocket.OPEN) return false;
      socket.send(JSON.stringify(frame));
      return true;
    },
    close() {
      stopped = true;
      clearTimeout(timer);
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("online", reconnectNow);
      socket?.close();
      socket = null;
    },
  };
}
