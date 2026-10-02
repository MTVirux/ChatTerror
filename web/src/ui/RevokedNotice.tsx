import { useState } from "preact/hooks";

export function RevokedNotice({ label, onRemove, onPairAgain, inline }: { label: string; onRemove: () => Promise<void>; onPairAgain: () => void; inline?: boolean }) {
  const [busy, setBusy] = useState(false);

  async function remove() {
    setBusy(true);
    try {
      await onRemove();
    } finally {
      setBusy(false);
    }
  }

  const Root = inline ? "div" : "main";
  return (
    <Root class={`status-panel${inline ? " inline" : ""}`}>
      <div class="banner" role="alert">
        <strong>{label} was removed from the plugin.</strong> Its messages were deleted from this phone.
      </div>
      <button class="btn primary wide" onClick={onPairAgain}>Pair again</button>
      <button class="btn ghost" onClick={remove} disabled={busy}>{busy ? "Removing..." : "Remove"}</button>
    </Root>
  );
}
