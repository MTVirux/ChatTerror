// Time-based so a restarted sender with a fresh counter still outruns what receivers have seen.
export class SeqCounter {
  private value: number;

  constructor(last = 0, private readonly nowMs: () => number = Date.now) {
    this.value = last;
  }

  get last(): number {
    return this.value;
  }

  next(): number {
    this.value = Math.max(this.value + 1, this.nowMs());
    return this.value;
  }
}

export class SeqGuard {
  constructor(private seen = 0) {}

  get lastSeen(): number {
    return this.seen;
  }

  accept(seq: number): boolean {
    if (seq <= this.seen) return false;
    this.seen = seq;
    return true;
  }
}
