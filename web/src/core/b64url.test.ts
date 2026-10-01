import { describe, expect, it } from "vitest";
import { decode, encode } from "./b64url";

describe("b64url", () => {
  it("encodes url-safe without padding", () => {
    expect(encode(new Uint8Array([0xfb, 0xff]))).toBe("-_8");
  });

  it("decodes url-safe without padding", () => {
    expect(Array.from(decode("-_8"))).toEqual([0xfb, 0xff]);
  });

  it("round trips every length up to 40", () => {
    for (let n = 0; n <= 40; n++) {
      const bytes = new Uint8Array(n).map((_, i) => (i * 37 + n) & 0xff);
      const text = encode(bytes);
      expect(text).not.toMatch(/[+/=]/);
      expect(Array.from(decode(text))).toEqual(Array.from(bytes));
    }
  });

  it("rejects invalid characters", () => {
    expect(() => decode("ab+c")).toThrow();
  });
});
