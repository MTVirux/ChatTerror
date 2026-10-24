import { describe, expect, it } from "vitest";
import { alertNotification, alertNotificationOptions, alertTag, DEFAULT_NOTIFY_PREFS, notifyPrefsWithDefaults } from "./alerts";

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

describe("alertNotificationOptions", () => {
  it("builds the title and options for an alert", () => {
    expect(alertNotificationOptions("dev1", { kind: "dutyPop", duty: "Sastasha" }, "Alt", true)).toEqual({
      title: "Alt - Duty ready",
      options: { body: "Sastasha", tag: "dev1:alert:dutyPop", renotify: true, icon: "/icon-192.png", data: { deviceId: "dev1" } },
    });
  });

  it("falls back when the duty is null", () => {
    expect(alertNotificationOptions("dev1", { kind: "dutyPop", duty: null }, "Main", false).options.body).toBe("Your duty is ready");
  });
});

describe("notifyPrefsWithDefaults", () => {
  it("uses the defaults when nothing is stored", () => {
    expect(notifyPrefsWithDefaults(undefined)).toEqual(DEFAULT_NOTIFY_PREFS);
    expect(notifyPrefsWithDefaults(null)).toEqual(DEFAULT_NOTIFY_PREFS);
    expect(notifyPrefsWithDefaults("on")).toEqual(DEFAULT_NOTIFY_PREFS);
  });

  it("merges stored prefs over the defaults", () => {
    expect(notifyPrefsWithDefaults({ dutyPop: false })).toEqual({ partyFull: true, dutyPop: false, waitSeconds: 10 });
    expect(notifyPrefsWithDefaults({ partyFull: false, dutyPop: false, waitSeconds: 0 })).toEqual({ partyFull: false, dutyPop: false, waitSeconds: 0 });
  });

  it("replaces invalid fields with the defaults", () => {
    expect(notifyPrefsWithDefaults({ partyFull: "no", dutyPop: 0, waitSeconds: 1.5 })).toEqual(DEFAULT_NOTIFY_PREFS);
    expect(notifyPrefsWithDefaults({ partyFull: false, waitSeconds: "30" })).toEqual({ partyFull: false, dutyPop: true, waitSeconds: 10 });
    expect(notifyPrefsWithDefaults({ waitSeconds: Number.NaN }).waitSeconds).toBe(10);
  });

  it("clamps the wait to 0 to 60 seconds", () => {
    expect(notifyPrefsWithDefaults({ waitSeconds: 90 }).waitSeconds).toBe(60);
    expect(notifyPrefsWithDefaults({ waitSeconds: -5 }).waitSeconds).toBe(0);
  });
});
