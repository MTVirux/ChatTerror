import { describe, expect, it } from "vitest";
import { sendErrorText } from "./format";

describe("sendErrorText", () => {
  it("explains a send while the game is offline", () => {
    expect(sendErrorText("gameOffline")).toBe("The game is offline");
  });

  it("falls back for unknown codes, including inherited object keys", () => {
    for (const code of ["nope", "constructor", "__proto__", "toString"]) {
      expect(sendErrorText(code)).toBe("Message failed to send");
    }
  });
});
