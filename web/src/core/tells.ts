import { decode, encode } from "./b64url";
import { openTell, sealTell } from "./crypto";
import { parseTellBody, type ChatItem, type TellBody, type TellBundle, type TellContact, type TellCopy } from "./protocol";
import type { AccountStore } from "./storage";

const utf8 = new TextEncoder();

function same(a: string, b: string): boolean {
  return a.localeCompare(b, undefined, { sensitivity: "accent" }) === 0;
}

// The sender's name comes from our own friend list, the sealed body could claim anything. The sender also sets ts,
// which must not pass our clock or it would move the sync point.
export function tellToItem(body: TellBody, from: string, contacts: TellContact[], now = Date.now()): ChatItem | null {
  const ts = Math.min(body.ts, now);
  if (contacts.some((c) => c.characterHash === from)) {
    return { id: body.id, ts, channel: "tell", sender: body.toName, senderWorld: body.toWorld, text: body.text, character: body.fromName, outgoing: true };
  }
  const contact = contacts.find((c) => c.hash === from && c.characterHash === body.toHash);
  if (!contact) return null;
  return { id: body.id, ts, channel: "tell", sender: contact.name, senderWorld: contact.world, text: body.text, character: contact.character, outgoing: false };
}

export function findContact(contacts: TellContact[], target: string, preferCharacter?: string): TellContact | undefined {
  const [name = "", world = ""] = target.split("@");
  const matches = contacts.filter((c) => same(c.name, name) && same(c.world, world));
  return matches.find((c) => c.character === preferCharacter) ?? matches[0];
}

export async function buildCopies(body: TellBody, recipient: TellBundle, own: TellBundle | null, ownTarget: string): Promise<TellCopy[]> {
  const plaintext = utf8.encode(JSON.stringify(body));
  const copies: TellCopy[] = [];
  const add = async (bundle: TellBundle, self: boolean) => {
    for (const entry of bundle.entries) {
      if (self && entry.target === ownTarget) continue;
      try {
        copies.push({ self, target: entry.target, envelope: encode(await sealTell(decode(entry.key), plaintext)) });
      } catch {
        // A bad key in a signed bundle only loses that one copy.
      }
    }
  };
  await add(recipient, false);
  if (own) await add(own, true);
  return copies;
}

// Shared by the session and the service worker. Null when the tell can't be opened, the sender is unknown, or the
// sender's character now sends from another install than the one trusted for it.
export async function openTellFrame(store: AccountStore, from: string, id: string, envelope: string, fromKey: string): Promise<ChatItem | null> {
  const [key, settings, pins] = await Promise.all([store.getMeta("tellKey"), store.getMeta("lastSettings"), store.getMeta("tellPins")]);
  const contacts = settings?.contacts ?? [];
  const own = contacts.some((c) => c.characterHash === from);
  const trusted = contacts.find((c) => c.hash === from)?.key ?? pins[from];
  if (!own && trusted !== undefined && trusted !== fromKey) return null;
  if (!key) return null;
  try {
    const plain = await openTell(key.privateKey, decode(key.publicKey), decode(envelope));
    const body = parseTellBody(JSON.parse(new TextDecoder().decode(plain)));
    if (!body || body.id !== id || body.fromHash !== from) return null;
    const item = tellToItem(body, from, contacts);
    if (item && !own && trusted === undefined) await store.setMeta("tellPins", { ...pins, [from]: fromKey });
    return item;
  } catch {
    return null;
  }
}
