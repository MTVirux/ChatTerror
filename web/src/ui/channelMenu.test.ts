import { describe, expect, it } from "vitest";
import { canMoveChannel } from "./ChannelMenu";
import { pickerChannels } from "./CustomChannelSheet";

describe("canMoveChannel", () => {
  const keys = ["x|A|tells", "x|A|fc", "x|A|party"];

  it("never moves Tells or anything above it", () => {
    expect(canMoveChannel(keys, "x|A|tells", 1)).toBe(false);
    expect(canMoveChannel(keys, "x|A|fc", -1)).toBe(false);
  });

  it("moves other channels within the list", () => {
    expect(canMoveChannel(keys, "x|A|fc", 1)).toBe(true);
    expect(canMoveChannel(keys, "x|A|party", -1)).toBe(true);
    expect(canMoveChannel(keys, "x|A|party", 1)).toBe(false);
    expect(canMoveChannel(keys, "x|A|gone", 1)).toBe(false);
  });
});

describe("pickerChannels", () => {
  it("offers relayed channels first and leaves tells out", () => {
    const list = pickerChannels(["party", "freeCompany"]);
    expect(list.slice(0, 2)).toEqual(["party", "freeCompany"]);
    expect(list).not.toContain("tell");
  });

  it("keeps tells for a channel that already shows them", () => {
    expect(pickerChannels(["tell", "party"], true).slice(0, 2)).toEqual(["tell", "party"]);
  });
});
