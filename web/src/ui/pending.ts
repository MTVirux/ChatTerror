import type { AccountManager } from "../core/accounts";
import type { CustomChannel } from "../core/channelPrefs";
import type { ChatChannel } from "../core/protocol";
import type { SubRow } from "./channels";
import { sendErrorText } from "./format";

export interface PendingSend {
  localId: number;
  deviceId: string;
  channel: ChatChannel;
  target?: string;
  character?: string;
  text: string;
  error?: string;
}

export interface PendingSends {
  list(): PendingSend[];
  subscribe(cb: (list: PendingSend[]) => void): () => void;
  send(deviceId: string, channel: ChatChannel, text: string, target?: string, character?: string): Promise<void>;
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
      result = await manager.session(entry.deviceId)!.send(entry.channel, entry.text, entry.target, entry.character);
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

    send(deviceId, channel, text, target, character) {
      const entry: PendingSend = { localId: nextLocalId++, deviceId, channel, text, target, character };
      set([...list, entry]);
      return run(entry);
    },

    retry(p) {
      const entry: PendingSend = { localId: p.localId, deviceId: p.deviceId, channel: p.channel, text: p.text, target: p.target, character: p.character };
      set(list.map((q) => (q.localId === p.localId ? entry : q)));
      return run(entry);
    },

    dismiss(localId) {
      set(list.filter((p) => p.localId !== localId));
    },
  };
}

export function pendingIn(p: PendingSend, deviceId: string, custom: CustomChannel | null, row: SubRow | null): boolean {
  if (p.deviceId !== deviceId) return false;
  // With no channels yet, failed sends still need somewhere to show Retry and Dismiss.
  if (!custom || !row) return true;
  if (p.character && p.character !== custom.character) return false;
  if (row.kind === "partner") return p.channel === "tell" && p.target === row.partner;
  if (row.kind === "type") return p.channel === row.channel;
  return custom.channels.includes(p.channel);
}
