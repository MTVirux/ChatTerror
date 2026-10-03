const HIT_TTL = 7 * 24 * 60 * 60 * 1000;
const MISS_TTL = 60 * 60 * 1000;

export const FETCHED_AT_HEADER = "x-fetched-at";

// Other errors like rate limits are worth retrying on the next view.
export function isCacheable(status: number): boolean {
  return status === 200 || status === 404;
}

export function isFresh(status: number, fetchedAt: number, now: number): boolean {
  return now - fetchedAt < (status === 200 ? HIT_TTL : MISS_TTL);
}
