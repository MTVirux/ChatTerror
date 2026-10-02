import { describe, expect, it } from "vitest";
import { initials, senderColor } from "./identity";

describe("identity", () => {
  it("takes the first letter of the first two words", () => {
    expect(initials("Y'shtola Rhul")).toBe("YR");
    expect(initials("Thancred Waters@Gilgamesh")).toBe("TW");
  });
  it("uses two letters of a single word and handles empty names", () => {
    expect(initials("Alpha")).toBe("AL");
    expect(initials("")).toBe("?");
  });
  it("gives a stable color per name", () => {
    expect(senderColor("Y'shtola Rhul")).toBe(senderColor("Y'shtola Rhul"));
    expect(senderColor("Y'shtola Rhul")).toMatch(/^#[0-9a-f]{6}$/);
  });
});
