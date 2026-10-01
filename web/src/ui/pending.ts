import type { AccountManager } from "../core/accounts";
import type { ChatChannel } from "../core/protocol";
import { sendErrorText } from "./format";

export interface PendingSend {
  localId: number;
  deviceId: string;
  channel: ChatChannel;
  target?: string;
  text: string;
  error?: string;
}

export interface PendingSends {
  list(): PendingSend[];
  subscribe(cb: (list: PendingSend[]) => void): () => void;
  send(deviceId: string, channel: ChatChannel, text: string, target?: string): Promise<void>;
  retry(p: PendingSend): Promise<void>;
  dismiss(localId: number): void;
}

// Lives above the chat views so a send survives switching account tabs.
export function createPendingSends(manager: AccountManager): PendingSends {
  let list: PendingSend[] = [];
  let nextLocalId = 1;
  const listeners = new Set<(list: PendingSend[]) => void>();

  function set(next: PendingSend[]) {
    list = next;
    for (const listener of listeners) listener(list);
  }

  async function run(entry: PendingSend) {
    let result;
    try {
      result = await manager.session(entry.deviceId)!.send(entry.channel, entry.text, entry.target);
    } catch {
      result = { ok: false, error: undefined };
    }
    if (result.ok) set(list.filter((p) => p.localId !== entry.localId));
    else set(list.map((p) => (p.localId === entry.localId ? { ...p, error: sendErrorText(result.error) } : p)));
  }

  return {
    list: () => list,

    subscribe(cb) {
      listeners.add(cb);
      return () => listeners.delete(cb);
    },

    send(deviceId, channel, text, target) {
      const entry: PendingSend = { localId: nextLocalId++, deviceId, channel, text, target };
      set([...list, entry]);
      return run(entry);
    },

    retry(p) {
      const entry: PendingSend = { localId: p.localId, deviceId: p.deviceId, channel: p.channel, text: p.text, target: p.target };
      set(list.map((q) => (q.localId === p.localId ? entry : q)));
      return run(entry);
    },

    dismiss(localId) {
      set(list.filter((p) => p.localId !== localId));
    },
  };
}
