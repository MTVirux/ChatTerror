import { describe, expect, it } from "vitest";
import { decode, encode } from "./b64url";
import { deriveKey, exportPublicRaw, fingerprint, generateDeviceKey, open, seal, sealWithNonce } from "./crypto";
import vector from "../../../test-vectors/e2e-v1.json";

const utf8 = new TextEncoder();

async function vectorKey(extractable = false) {
  const devicePrivate = await crypto.subtle.importKey("jwk", vector.device.jwk, { name: "ECDH", namedCurve: "P-256" }, false, ["deriveBits"]);
  return deriveKey(devicePrivate, decode(vector.plugin.publicRaw), decode(vector.device.publicRaw), extractable);
}

describe("crypto", () => {
  it("derives the vector key", async () => {
    const key = await vectorKey(true);
    const raw = new Uint8Array(await crypto.subtle.exportKey("raw", key));
    expect(encode(raw)).toBe(vector.key);
  });

  it("derived key is not extractable by default", async () => {
    expect((await vectorKey()).extractable).toBe(false);
  });

  it("computes the vector fingerprint", async () => {
    expect(await fingerprint(vector.secret, decode(vector.plugin.publicRaw), decode(vector.device.publicRaw))).toBe(vector.fingerprint);
  });

  it("fingerprint depends on the secret and normalizes it", async () => {
    const plugin = decode(vector.plugin.publicRaw);
    const device = decode(vector.device.publicRaw);
    const base = await fingerprint("ABCD2345", plugin, device);
    expect(await fingerprint("abcd-2345", plugin, device)).toBe(base);
    expect(await fingerprint("ABCD2346", plugin, device)).not.toBe(base);
    await expect(fingerprint("ABCD234", plugin, device)).rejects.toThrow();
  });

  it("opens the vector envelope", async () => {
    const plain = await open(await vectorKey(), "p2d", decode(vector.seal.envelope));
    expect(new TextDecoder().decode(plain)).toBe(vector.seal.plaintext);
  });

  it("seals with the vector nonce to the vector envelope", async () => {
    const sealed = await sealWithNonce(await vectorKey(), "p2d", utf8.encode(vector.seal.plaintext), decode(vector.seal.nonce));
    expect(encode(sealed)).toBe(vector.seal.envelope);
  });

  it("open with the wrong direction throws", async () => {
    await expect(open(await vectorKey(), "d2p", decode(vector.seal.envelope))).rejects.toThrow();
  });

  it("open rejects short or wrong-version envelopes", async () => {
    const key = await vectorKey();
    await expect(open(key, "p2d", new Uint8Array(10))).rejects.toThrow();
    const envelope = decode(vector.seal.envelope);
    envelope[0] = 2;
    await expect(open(key, "p2d", envelope)).rejects.toThrow();
  });

  it("generated keys round trip through seal and open", async () => {
    const plugin = await generateDeviceKey();
    const device = await generateDeviceKey();
    expect(device.privateKey.extractable).toBe(false);
    const pluginPub = await exportPublicRaw(plugin.publicKey);
    const devicePub = await exportPublicRaw(device.publicKey);
    expect(pluginPub.length).toBe(65);
    expect(pluginPub[0]).toBe(4);

    const key = await deriveKey(device.privateKey, pluginPub, devicePub);
    const sealed = await seal(key, "d2p", utf8.encode("hi"));
    expect(sealed[0]).toBe(1);
    expect(sealed.length).toBe(1 + 12 + 2 + 16);
    expect(new TextDecoder().decode(await open(key, "d2p", sealed))).toBe("hi");
  });
});
