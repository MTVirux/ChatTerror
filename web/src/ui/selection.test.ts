import { describe, expect, it } from "vitest";
import type { AccountView } from "../core/accounts";
import { initialSelection, validSelection } from "./selection";

const acct = (deviceId: string) => ({ deviceId, label: deviceId, status: "active", unread: 0, state: {} }) as unknown as AccountView;

describe("selection", () => {
  it("opens the add tab for a pair link when accounts exist", () => {
    expect(initialSelection([acct("a")], "#pair=ABCD", null)).toBe("add");
  });
  it("opens the account from a notification link", () => {
    expect(initialSelection([acct("a"), acct("b")], "#account=b", "a")).toBe("b");
  });
  it("restores the stored tab when it still exists", () => {
    expect(initialSelection([acct("a"), acct("b")], "", "b")).toBe("b");
    expect(initialSelection([acct("a"), acct("b")], "", "all")).toBe("all");
  });
  it("falls back to the first account", () => {
    expect(initialSelection([acct("a"), acct("b")], "", "gone")).toBe("a");
    expect(initialSelection([], "", null)).toBe("add");
  });
  it("leaves a removed account", () => {
    expect(validSelection("a", [acct("b")])).toBe("b");
    expect(validSelection("all", [acct("b")])).toBe("b");
    expect(validSelection("all", [acct("a"), acct("b")])).toBe("all");
    expect(validSelection("add", [])).toBe("add");
  });
});
