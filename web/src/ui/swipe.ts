export const SWIPE_PX = 40;

export function swipeResult({ dx, dy, open }: { dx: number; dy: number; open: boolean }): "open" | "close" | null {
  if (Math.abs(dx) <= Math.abs(dy)) return null;
  if (!open && dx >= SWIPE_PX) return "open";
  if (open && dx <= -SWIPE_PX) return "close";
  return null;
}

// Dragging inside a field moves the caret or selection, not the drawer.
export function ignoresSwipe(tagName: string): boolean {
  return tagName === "INPUT" || tagName === "TEXTAREA" || tagName === "SELECT";
}
