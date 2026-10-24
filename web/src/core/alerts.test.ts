import { describe, expect, it } from "vitest";
import { alertNotification, alertTag } from "./alerts";

describe("alertNotification", () => {
  it("names the duty that popped", () => {
    expect(alertNotification({ kind: "dutyPop", duty: "The Aurum Vale" }, "Main", false)).toEqual({ title: "Duty ready", body: "The Aurum Vale" });
  });

  it("falls back when the duty is unknown", () => {
    expect(alertNotification({ kind: "dutyPop" }, "Main", false)).toEqual({ title: "Duty ready", body: "Your duty is ready" });
  });

  it("says the party filled", () => {
    expect(alertNotification({ kind: "partyFull" }, "Main", false)).toEqual({ title: "Party is full", body: "Your Party Finder party has filled up" });
  });

  it("prefixes the account label with several accounts", () => {
    expect(alertNotification({ kind: "partyFull" }, "Main", true).title).toBe("Main - Party is full");
    expect(alertNotification({ kind: "dutyPop", duty: "Sastasha" }, "Alt", true)).toEqual({ title: "Alt - Duty ready", body: "Sastasha" });
  });
});

describe("alertTag", () => {
  it("is per account and kind", () => {
    expect(alertTag("dev1", "dutyPop")).toBe("dev1:alert:dutyPop");
    expect(alertTag("dev1", "partyFull")).toBe("dev1:alert:partyFull");
  });
});
