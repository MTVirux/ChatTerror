import { decode, encode } from "./b64url";
import { isTellBundle, type SignedBundle, type TellBundle } from "./protocol";

export type Direction = "p2d" | "d2p";

const VERSION = 1;
const NONCE_SIZE = 12;
const TAG_SIZE = 16;
const HEADER_SIZE = 1 + NONCE_SIZE;
const ECDH = { name: "ECDH", namedCurve: "P-256" } as const;
const utf8 = new TextEncoder();
const INFO = utf8.encode("ChatTerror v1");

function aad(direction: Direction): Uint8Array<ArrayBuffer> {
  return utf8.encode(direction === "p2d" ? "ct1:p2d" : "ct1:d2p");
}

function concat(a: Uint8Array, b: Uint8Array): Uint8Array<ArrayBuffer> {
  const out = new Uint8Array(a.length + b.length);
  out.set(a);
  out.set(b, a.length);
  return out;
}

export function generateDeviceKey(): Promise<CryptoKeyPair> {
  return crypto.subtle.generateKey(ECDH, false, ["deriveBits"]);
}

export async function exportPublicRaw(publicKey: CryptoKey): Promise<Uint8Array<ArrayBuffer>> {
  return new Uint8Array(await crypto.subtle.exportKey("raw", publicKey));
}

export async function importPublicRaw(raw: Uint8Array<ArrayBuffer>): Promise<CryptoKey> {
  if (raw.length !== 65 || raw[0] !== 4) throw new Error("Invalid P-256 public key.");
  return crypto.subtle.importKey("raw", raw, ECDH, true, []);
}

// Device side: the peer is always the plugin.
export async function deriveKey(
  privateKey: CryptoKey,
  pluginPubRaw: Uint8Array<ArrayBuffer>,
  devicePubRaw: Uint8Array<ArrayBuffer>,
  extractable = false,
): Promise<CryptoKey> {
  const peer = await importPublicRaw(pluginPubRaw);
  const z = await crypto.subtle.deriveBits({ name: "ECDH", public: peer }, privateKey, 256);
  const ikm = await crypto.subtle.importKey("raw", z, "HKDF", false, ["deriveKey"]);
  return crypto.subtle.deriveKey(
    { name: "HKDF", hash: "SHA-256", salt: concat(pluginPubRaw, devicePubRaw), info: INFO },
    ikm,
    { name: "AES-GCM", length: 256 },
    extractable,
    ["encrypt", "decrypt"],
  );
}

export function seal(key: CryptoKey, direction: Direction, plaintext: Uint8Array<ArrayBuffer>): Promise<Uint8Array<ArrayBuffer>> {
  return sealWithNonce(key, direction, plaintext, crypto.getRandomValues(new Uint8Array(NONCE_SIZE)));
}

export async function sealWithNonce(
  key: CryptoKey,
  direction: Direction,
  plaintext: Uint8Array<ArrayBuffer>,
  nonce: Uint8Array<ArrayBuffer>,
): Promise<Uint8Array<ArrayBuffer>> {
  const sealed = await crypto.subtle.encrypt({ name: "AES-GCM", iv: nonce, additionalData: aad(direction) }, key, plaintext);
  return concat(concat(new Uint8Array([VERSION]), nonce), new Uint8Array(sealed));
}

export async function open(key: CryptoKey, direction: Direction, envelope: Uint8Array<ArrayBuffer>): Promise<Uint8Array<ArrayBuffer>> {
  if (envelope.length < HEADER_SIZE + TAG_SIZE) throw new Error("Envelope too short.");
  if (envelope[0] !== VERSION) throw new Error("Unsupported envelope version.");
  const plain = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: envelope.slice(1, HEADER_SIZE), additionalData: aad(direction) },
    key,
    envelope.slice(HEADER_SIZE),
  );
  return new Uint8Array(plain);
}

export async function sealPayload(key: CryptoKey, direction: Direction, payload: unknown): Promise<string> {
  return encode(await seal(key, direction, utf8.encode(JSON.stringify(payload))));
}

// Throws when the envelope is malformed, fails authentication or is not JSON.
export async function openPayload(key: CryptoKey, direction: Direction, envelope: string): Promise<unknown> {
  const plain = await open(key, direction, decode(envelope));
  return JSON.parse(new TextDecoder().decode(plain));
}

export function normalizeSecret(input: string): string | null {
  const raw = input.replace(/[\s-]/g, "").toUpperCase().replace(/O/g, "0").replace(/[IL]/g, "1");
  return /^[0-9A-HJKMNP-TV-Z]{8}$/.test(raw) ? raw : null;
}

// Mixing in the pairing secret, which the relay never sees, stops a relay from grinding keys to match the number.
export async function fingerprint(secret: string, pluginPubRaw: Uint8Array<ArrayBuffer>, devicePubRaw: Uint8Array<ArrayBuffer>): Promise<string> {
  const normalized = normalizeSecret(secret);
  if (!normalized) throw new Error("Invalid pairing secret");
  const hash = await crypto.subtle.digest("SHA-256", concat(utf8.encode(normalized), concat(pluginPubRaw, devicePubRaw)));
  const digits = String(new DataView(hash).getUint32(0) % 1_000_000).padStart(6, "0");
  return `${digits.slice(0, 3)} ${digits.slice(3)}`;
}

const TELL_VERSION = 2;
const KEY_SIZE = 65;
const TELL_HEADER = 1 + KEY_SIZE + NONCE_SIZE;
const TELL_INFO = utf8.encode("ChatTerror tell v1");
const TELL_AAD = utf8.encode("ct1:tell");

export function generateTellKey(): Promise<CryptoKeyPair> {
  return crypto.subtle.generateKey(ECDH, false, ["deriveBits"]);
}

async function tellKey(
  privateKey: CryptoKey,
  peerRaw: Uint8Array<ArrayBuffer>,
  ephemeralRaw: Uint8Array<ArrayBuffer>,
  recipientRaw: Uint8Array<ArrayBuffer>,
): Promise<CryptoKey> {
  const peer = await importPublicRaw(peerRaw);
  const z = await crypto.subtle.deriveBits({ name: "ECDH", public: peer }, privateKey, 256);
  const ikm = await crypto.subtle.importKey("raw", z, "HKDF", false, ["deriveKey"]);
  return crypto.subtle.deriveKey(
    { name: "HKDF", hash: "SHA-256", salt: concat(ephemeralRaw, recipientRaw), info: TELL_INFO },
    ikm,
    { name: "AES-GCM", length: 256 },
    false,
    ["encrypt", "decrypt"],
  );
}

// ephemeral and nonce are only passed by tests.
export async function sealTell(
  recipientRaw: Uint8Array<ArrayBuffer>,
  plaintext: Uint8Array<ArrayBuffer>,
  ephemeral?: CryptoKeyPair,
  nonce?: Uint8Array<ArrayBuffer>,
): Promise<Uint8Array<ArrayBuffer>> {
  const pair = ephemeral ?? (await crypto.subtle.generateKey(ECDH, false, ["deriveBits"]));
  const ephemeralRaw = await exportPublicRaw(pair.publicKey);
  const key = await tellKey(pair.privateKey, recipientRaw, ephemeralRaw, recipientRaw);
  const iv = nonce ?? crypto.getRandomValues(new Uint8Array(NONCE_SIZE));
  const sealed = await crypto.subtle.encrypt({ name: "AES-GCM", iv, additionalData: TELL_AAD }, key, plaintext);
  return concat(concat(concat(new Uint8Array([TELL_VERSION]), ephemeralRaw), iv), new Uint8Array(sealed));
}

export async function openTell(
  privateKey: CryptoKey,
  recipientRaw: Uint8Array<ArrayBuffer>,
  envelope: Uint8Array<ArrayBuffer>,
): Promise<Uint8Array<ArrayBuffer>> {
  if (envelope.length < TELL_HEADER + TAG_SIZE) throw new Error("Envelope too short.");
  if (envelope[0] !== TELL_VERSION) throw new Error("Unsupported envelope version.");
  const ephemeralRaw = envelope.slice(1, 1 + KEY_SIZE);
  const key = await tellKey(privateKey, ephemeralRaw, ephemeralRaw, recipientRaw);
  const plain = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: envelope.slice(1 + KEY_SIZE, TELL_HEADER), additionalData: TELL_AAD },
    key,
    envelope.slice(TELL_HEADER),
  );
  return new Uint8Array(plain);
}

// Null when malformed, not signed by its own install key, or that key is not the expected one.
export async function verifyBundle(signed: SignedBundle, expectedKey?: string): Promise<TellBundle | null> {
  try {
    const bytes = decode(signed.bundle);
    const bundle: unknown = JSON.parse(new TextDecoder().decode(bytes));
    if (!isTellBundle(bundle) || (expectedKey !== undefined && bundle.installPublicKey !== expectedKey)) return null;
    const key = await crypto.subtle.importKey("raw", decode(bundle.installPublicKey), { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
    const ok = await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, key, decode(signed.signature), bytes);
    return ok ? bundle : null;
  } catch {
    return null;
  }
}
