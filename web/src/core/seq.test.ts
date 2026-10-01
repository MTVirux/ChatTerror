import { describe, expect, it } from "vitest";
import { SeqCounter, SeqGuard } from "./seq";

describe("seq", () => {
  it("is monotonic with a frozen clock", () => {
    const counter = new SeqCounter(5000, () => 1000);
    expect(counter.next()).toBe(5001);
    expect(counter.next()).toBe(5002);
    expect(counter.next()).toBe(5003);
    expect(counter.last).toBe(5003);
  });

  it("uses the clock when ahead of last", () => {
    let now = 1000;
    const counter = new SeqCounter(0, () => now);
    expect(counter.next()).toBe(1000);
    now = 5000;
    expect(counter.next()).toBe(5000);
  });

  it("is greater after a restart", () => {
    const guard = new SeqGuard();
    const before = new SeqCounter(0, () => 1000);
    expect(before.next()).toBe(1000);
    expect(guard.accept(1000)).toBe(true);

    const afterRestart = new SeqCounter(0, () => 1001);
    const seq = afterRestart.next();
    expect(seq).toBe(1001);
    expect(guard.accept(seq)).toBe(true);
  });

  it("guard rejects replay and older", () => {
    const guard = new SeqGuard(10);
    expect(guard.accept(10)).toBe(false);
    expect(guard.accept(9)).toBe(false);
    expect(guard.accept(11)).toBe(true);
    expect(guard.accept(11)).toBe(false);
    expect(guard.lastSeen).toBe(11);
  });
});
