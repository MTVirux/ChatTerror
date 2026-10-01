import { describe, expect, it } from "vitest";
import { parsePairCode } from "./session";

describe("parsePairCode", () => {
  it("accepts a lowercase hyphenated code", () => {
    expect(parsePairCode("abcd-efgh")).toBe("ABCD-EFGH");
  });

  it("accepts a code without hyphen", () => {
    expect(parsePairCode("ABCDEFGH")).toBe("ABCD-EFGH");
  });

  it("trims whitespace", () => {
    expect(parsePairCode("  7k3m 9z2q ")).toBe("7K3M-9Z2Q");
  });

  it("accepts a pairing URL", () => {
    expect(parsePairCode("https://relay.example.com/#pair=WXYZ-2345")).toBe("WXYZ-2345");
    expect(parsePairCode("https://relay.example.com/?x=1#pair=wxyz2345")).toBe("WXYZ-2345");
  });

  it("maps ambiguous Crockford letters", () => {
    expect(parsePairCode("O1IL-ABCD")).toBe("0111-ABCD");
  });

  it("rejects wrong lengths and invalid characters", () => {
    expect(parsePairCode("")).toBeNull();
    expect(parsePairCode("ABC-DEFG")).toBeNull();
    expect(parsePairCode("ABCD-EFGHJ")).toBeNull();
    expect(parsePairCode("ABCD-EFGU")).toBeNull();
    expect(parsePairCode("ABCD_EFGH")).toBeNull();
    expect(parsePairCode("https://relay.example.com/")).toBeNull();
  });
});
