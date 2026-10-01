import { useState } from "preact/hooks";
import type { Session, SessionState } from "../core/session";

export function PendingScreen({ session, state, onCancelled }: { session: Session; state: SessionState; onCancelled: () => void }) {
  const [busy, setBusy] = useState(false);
  const groups = (state.fingerprint ?? "--- ---").split(" ");

  async function cancel() {
    setBusy(true);
    try {
      await session.unpair();
    } finally {
      onCancelled();
    }
  }

  return (
    <main class="screen pending">
      <p class="waiting"><span class="pulse" aria-hidden="true" /> Waiting for approval</p>
      <div class="fingerprint" aria-label={`Security number ${groups.join(" ")}`}>
        {groups.map((g) => <span class="fp-group">{g}</span>)}
      </div>
      <p class="pending-text">Confirm this number matches in the game, then approve there.</p>
      <p class="hint">If the numbers are different, don't approve. Cancel and pair again.</p>
      <button class="btn ghost" onClick={cancel} disabled={busy}>
        {busy ? "Cancelling..." : "Cancel pairing"}
      </button>
    </main>
  );
}
