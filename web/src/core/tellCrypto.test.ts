import { describe, expect, it } from "vitest";
import { decode, encode } from "./b64url";
import { exportPublicRaw, generateTellKey, importPublicRaw, openTell, sealTell, verifyBundle } from "./crypto";
import { isValidSettings, parseServerFrame, parseTellBody } from "./protocol";
import vector from "../../../test-vectors/tell-v1.json";

const ECDH = { name: "ECDH", namedCurve: "P-256" } as const;
const utf8 = new TextEncoder();

describe("sealed tells", () => {
  it("matches the shared vector", async () => {
    const recipient = await crypto.subtle.importKey("jwk", vector.recipient.jwk, ECDH, false, ["deriveBits"]);
    const ephemeral = {
      privateKey: await crypto.subtle.importKey("jwk", vector.ephemeral.jwk, ECDH, false, ["deriveBits"]),
      publicKey: await importPublicRaw(decode(vector.ephemeral.publicRaw)),
    };
    const recipientRaw = decode(vector.recipient.publicRaw);

    const sealed = await sealTell(recipientRaw, utf8.encode(vector.plaintext), ephemeral, decode(vector.nonce));
    expect(encode(sealed)).toBe(vector.envelope);
    expect(new TextDecoder().decode(await openTell(recipient, recipientRaw, decode(vector.envelope)))).toBe(vector.plaintext);
    expect(await verifyBundle(vector.bundle, vector.recipient.publicRaw)).not.toBeNull();
    expect(await verifyBundle(vector.bundle, encode(new Uint8Array(65)))).toBeNull();
  });

  it("round trips with a fresh key and rejects tampering", async () => {
    const keys = await generateTellKey();
    const raw = await exportPublicRaw(keys.publicKey);
    const sealed = await sealTell(raw, utf8.encode("hi"));
    expect(new TextDecoder().decode(await openTell(keys.privateKey, raw, sealed))).toBe("hi");
    sealed[sealed.length - 1] ^= 1;
    await expect(openTell(keys.privateKey, raw, sealed)).rejects.toThrow();
  });

  it("parses tell frames and bodies", () => {
    expect(parseServerFrame('{"t":"tell","id":"a","from":"b","envelope":"c","fromKey":"k"}')).toEqual({ t: "tell", id: "a", from: "b", envelope: "c", fromKey: "k" });
    expect(parseServerFrame('{"t":"tell","id":"a","from":"b","envelope":"c"}')).toBeNull();
    expect(parseServerFrame('{"t":"tell","id":"a"}')).toBeNull();
    expect(parseServerFrame('{"t":"tellResult","id":"a","ok":false,"error":"notPaired"}')).not.toBeNull();
    expect(parseTellBody(JSON.parse(vector.plaintext))?.text).toBe("hello <3");
    expect(parseTellBody({ id: "x" })).toBeNull();
  });

  it("validates contacts in settings", () => {
    const base = { relayChannels: [], sendChannels: [], maxLength: 500 };
    const contact = { character: "A B", characterWorld: "Lich", characterHash: "h1", name: "C D", world: "Lich", hash: "h2", installId: "i1", key: "k1" };
    expect(isValidSettings({ ...base, contacts: [contact] })).toBe(true);
    expect(isValidSettings({ ...base, contacts: [{ ...contact, hash: 3 }] })).toBe(false);
    expect(isValidSettings(base)).toBe(true);
    for (const bad of [
      { name: "C|D" },
      { world: "x".repeat(65) },
      { character: 1 },
      { characterWorld: null },
      { hash: "h".repeat(44) },
      { characterHash: "h".repeat(44) },
      { key: "k".repeat(88) },
      { key: 5 },
      { installId: "i".repeat(65) },
    ]) {
      expect(isValidSettings({ ...base, contacts: [{ ...contact, ...bad }] })).toBe(false);
    }
    expect(isValidSettings({ ...base, contacts: [{ ...contact, hash: "h".repeat(43), key: "k".repeat(87) }] })).toBe(true);
  });

  it("rejects tell bodies with bad timestamps or fields", () => {
    const body = JSON.parse(vector.plaintext);
    expect(parseTellBody({ ...body, ts: Date.now() + 1e9 })).not.toBeNull();
    for (const ts of [-Infinity, Infinity, NaN, -1, -1e20, "1"]) expect(parseTellBody({ ...body, ts })).toBeNull();
    expect(parseTellBody({ ...body, toName: "A|B" })).toBeNull();
    expect(parseTellBody({ ...body, fromWorld: "x".repeat(65) })).toBeNull();
    expect(parseTellBody({ ...body, fromHash: "h".repeat(44) })).toBeNull();
    expect(parseTellBody({ ...body, id: "i".repeat(65) })).toBeNull();
    expect(parseTellBody({ ...body, text: "x".repeat(5000) })).toBeNull();
  });
});
