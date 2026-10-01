import "fake-indexeddb/auto";
import { IDBFactory } from "fake-indexeddb";
import { describe, expect, it } from "vitest";
import type { Api } from "./api";
import { decode, encode } from "./b64url";
import { exportPublicRaw, fingerprint, generateDeviceKey } from "./crypto";
import { formatPairCode, pairDevice, parsePairCode } from "./session";
import { openAccountStore, resetStorageForTests } from "./storage";

describe("parsePairCode", () => {
  it("accepts a lowercase hyphenated code", () => {
    expect(parsePairCode("abcd-efgh-2345-6789")).toEqual({ code: "ABCD-EFGH", secret: "23456789" });
  });

  it("accepts a code without hyphens", () => {
    expect(parsePairCode("ABCDEFGH23456789")).toEqual({ code: "ABCD-EFGH", secret: "23456789" });
  });

  it("trims whitespace", () => {
    expect(parsePairCode("  7k3m 9z2q wxyz 0000 ")).toEqual({ code: "7K3M-9Z2Q", secret: "WXYZ0000" });
  });

  it("accepts a pairing URL", () => {
    expect(parsePairCode("https://relay.example.com/#pair=WXYZ-2345-ABCD-EFGH")).toEqual({ code: "WXYZ-2345", secret: "ABCDEFGH" });
    expect(parsePairCode("https://relay.example.com/?x=1#pair=wxyz2345abcdefgh")).toEqual({ code: "WXYZ-2345", secret: "ABCDEFGH" });
  });

  it("maps ambiguous Crockford letters", () => {
    expect(parsePairCode("O1IL-ABCD-OOII-LLLL")).toEqual({ code: "0111-ABCD", secret: "00111111" });
  });

  it("rejects a code without the secret part", () => {
    expect(parsePairCode("ABCD-EFGH")).toBeNull();
    expect(parsePairCode("https://relay.example.com/#pair=WXYZ-2345")).toBeNull();
  });

  it("rejects wrong lengths and invalid characters", () => {
    expect(parsePairCode("")).toBeNull();
    expect(parsePairCode("ABCD-EFGH-2345-678")).toBeNull();
    expect(parsePairCode("ABCD-EFGH-2345-6789J")).toBeNull();
    expect(parsePairCode("ABCD-EFGH-2345-678U")).toBeNull();
    expect(parsePairCode("ABCD_EFGH_2345_6789")).toBeNull();
    expect(parsePairCode("https://relay.example.com/")).toBeNull();
  });

  it("formats both parts for display", () => {
    expect(formatPairCode({ code: "ABCD-EFGH", secret: "23456789" })).toBe("ABCD-EFGH-2345-6789");
  });
});

describe("pairDevice", () => {
  it("sends only the relay code and stores the secret-bound fingerprint", async () => {
    const plugin = await generateDeviceKey();
    const pluginPublicKey = encode(await exportPublicRaw(plugin.publicKey));
    const seen: string[] = [];
    let claimedKey = "";
    const fail = () => Promise.reject(new Error("not expected"));
    const api: Api = {
      lookupPairing: (code) => {
        seen.push(code);
        return Promise.resolve({ installId: "inst", pluginPublicKey });
      },
      claimPairing: (code, devicePublicKey) => {
        seen.push(code);
        claimedKey = devicePublicKey;
        return Promise.resolve({ deviceId: "dev", deviceToken: "d.dev.s" });
      },
      getMe: fail,
      deleteDevice: fail,
      putPush: fail,
      deletePush: fail,
      getVapid: fail,
    };

    resetStorageForTests();
    globalThis.indexedDB = new IDBFactory();
    const store = openAccountStore("chatterror-test");
    const result = await pairDevice(api, store, "abcd-efgh-2345-6789", "Phone");

    expect(seen).toEqual(["ABCD-EFGH", "ABCD-EFGH"]);
    const expected = await fingerprint("23456789", decode(pluginPublicKey), decode(claimedKey));
    expect(result.fingerprint).toBe(expected);
    expect((await store.getPairing())?.fingerprint).toBe(expected);
  });

  it("rejects a code missing the secret", async () => {
    const fail = () => Promise.reject(new Error("not expected"));
    const api = { lookupPairing: fail, claimPairing: fail } as unknown as Api;
    await expect(pairDevice(api, openAccountStore("chatterror-test"), "ABCD-EFGH", "Phone")).rejects.toMatchObject({ code: "invalidCode" });
  });
});
