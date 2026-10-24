import { describe, expect, it } from "vitest";
import { listTime, sendErrorText, timeOfDay } from "./format";

describe("sendErrorText", () => {
  it("explains a send while the game is offline", () => {
    expect(sendErrorText("gameOffline")).toBe("The game is offline");
  });

  it("explains relayed tell failures", () => {
    for (const code of ["notPaired", "notChatTerror", "keyChanged"]) {
      expect(sendErrorText(code)).not.toBe("Message failed to send");
    }
  });

  it("falls back for unknown codes, including inherited object keys", () => {
    for (const code of ["nope", "constructor", "__proto__", "toString"]) {
      expect(sendErrorText(code)).toBe("Message failed to send");
    }
  });
});

describe("listTime", () => {
  it("labels list times by day", () => {
    const now = new Date(2026, 9, 24, 21, 0).getTime();
    const today = new Date(2026, 9, 24, 8, 5).getTime();
    const yesterday = new Date(2026, 9, 23, 23, 0).getTime();
    const older = new Date(2026, 9, 1, 12, 0).getTime();
    expect(listTime(today, now)).toBe(timeOfDay(today));
    expect(listTime(yesterday, now)).toBe("Yesterday");
    expect(listTime(older, now)).toBe(new Date(older).toLocaleDateString(undefined, { day: "numeric", month: "short" }));
  });
});
