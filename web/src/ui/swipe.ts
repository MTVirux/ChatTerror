export const EDGE_PX = 24;
export const SWIPE_PX = 40;

export function swipeResult({ startX, dx, dy, open }: { startX: number; dx: number; dy: number; open: boolean }): "open" | "close" | null {
  if (Math.abs(dx) <= Math.abs(dy)) return null;
  if (!open && startX <= EDGE_PX && dx >= SWIPE_PX) return "open";
  if (open && dx <= -SWIPE_PX) return "close";
  return null;
}
