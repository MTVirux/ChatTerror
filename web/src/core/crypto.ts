import { decode, encode } from "./b64url";

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
