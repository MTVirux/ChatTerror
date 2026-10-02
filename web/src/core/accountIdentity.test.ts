import { describe, expect, it } from "vitest";
import { accountColors, hashColor, PALETTE, uniqueLabels } from "./accountIdentity";

describe("uniqueLabels", () => {
  it("keeps distinct labels as they are", () => {
    expect(uniqueLabels([{ fallback: "Alpha" }, { name: "Main", fallback: "Bravo" }])).toEqual(["Alpha", "Main"]);
  });

  it("numbers repeated labels by pairing order", () => {
    expect(uniqueLabels([{ fallback: "Alpha" }, { fallback: "Alpha" }, { fallback: "Alpha" }])).toEqual(["Alpha", "Alpha (2)", "Alpha (3)"]);
  });

  it("lets a custom name win over a character name", () => {
    expect(uniqueLabels([{ fallback: "Alpha" }, { name: "Alpha", fallback: "Bravo" }])).toEqual(["Alpha (2)", "Alpha"]);
  });

  it("treats case, spacing and invisible characters as the same label", () => {
    expect(uniqueLabels([{ fallback: "Alpha Beta" }, { fallback: " alpha​  BETA" }])).toEqual(["Alpha Beta", " alpha​  BETA (2)"]);
  });

  it("skips numbers another label already uses", () => {
    expect(uniqueLabels([{ fallback: "Alpha (2)" }, { fallback: "Alpha" }, { fallback: "Alpha" }])).toEqual(["Alpha (2)", "Alpha", "Alpha (3)"]);
  });
});

describe("accountColors", () => {
  it("uses the hashed color when it is free", () => {
    expect(accountColors(["a"])).toEqual([hashColor("a")]);
  });

  it("gives later accounts an unused color on a collision", () => {
    const ids = ["a", "b", "c", "d", "e", "f", "g", "h"];
    const colors = accountColors(ids);
    expect(new Set(colors).size).toBe(PALETTE.length);
    expect(colors[0]).toBe(hashColor("a"));
  });

  it("reuses colors once the palette runs out", () => {
    const ids = Array.from({ length: PALETTE.length + 1 }, (_, i) => `id${i}`);
    expect(accountColors(ids)).toHaveLength(PALETTE.length + 1);
  });
});
