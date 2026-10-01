import { useEffect, useState } from "preact/hooks";
import { openSession, type Session, type SessionState } from "../core/session";
import { PairScreen } from "./PairScreen";
import { PendingScreen } from "./PendingScreen";
import { ChatView } from "./ChatView";
import { loadCacheLimit } from "./theme";

function useSessionState(session: Session): SessionState {
  const [state, setState] = useState(session.getState());
  useEffect(() => {
    setState(session.getState());
    return session.subscribe(setState);
  }, [session]);
  return state;
}

export function App({ initial }: { initial: Session }) {
  const [session, setSession] = useState(initial);
  const state = useSessionState(session);

  const paired = state.status !== "unpaired" && state.status !== "revoked";

  // Pairing and unpairing wipe the core's stored cache limit, so the saved choice is reapplied.
  useEffect(() => {
    if (paired) session.setCacheLimit(loadCacheLimit()).catch(() => {});
  }, [session, paired]);

  async function reopen() {
    session.close();
    setSession(await openSession());
  }

  if (state.status === "unpaired" || state.status === "revoked") {
    return <PairScreen revoked={state.status === "revoked"} />;
  }
  if (state.status === "pending") {
    return <PendingScreen session={session} state={state} onCancelled={reopen} />;
  }
  return <ChatView session={session} state={state} onUnpaired={reopen} />;
}
