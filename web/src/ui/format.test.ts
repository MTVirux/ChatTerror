import { describe, expect, it } from "vitest";
import { sendErrorText } from "./format";

describe("sendErrorText", () => {
  it("explains a send while the game is offline", () => {
    expect(sendErrorText("gameOffline")).toBe("The game is offline");
  });
});
