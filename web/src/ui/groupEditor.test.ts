import { describe, expect, it } from "vitest";
import { channelLabel } from "./format";
import { defaultName, pickerChannels } from "./GroupEditor";

describe("pickerChannels", () => {
  it("offers relayed channels first and never tells", () => {
    const list = pickerChannels(["party", "freeCompany", "tell"]);
    expect(list.slice(0, 2)).toEqual(["party", "freeCompany"]);
    expect(list).not.toContain("tell");
  });
});

describe("defaultName", () => {
  it("names a group after its channels", () => {
    expect(defaultName(["linkshell1", "freeCompany"])).toBe(`${channelLabel("linkshell1")}, ${channelLabel("freeCompany")}`);
  });
});
