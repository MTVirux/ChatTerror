import { useEffect, useMemo, useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import type { Session } from "../core/session";
import { PairScreen } from "./PairScreen";
import { PendingScreen } from "./PendingScreen";
import { ChatView } from "./ChatView";

function useAccounts(manager: AccountManager): AccountView[] {
  const [accounts, setAccounts] = useState(manager.list());
  useEffect(() => {
    setAccounts(manager.list());
    return manager.subscribe(setAccounts);
  }, [manager]);
  return accounts;
}

export function App({ manager }: { manager: AccountManager }) {
  const accounts = useAccounts(manager);
  const account = accounts.find((a) => a.status === "active") ?? accounts[0];
  const raw = account && manager.session(account.deviceId);

  // Cache limit and unpair go through the manager so the registry stays in sync.
  const session = useMemo<Session | undefined>(
    () => raw && { ...raw, setCacheLimit: (n) => manager.setCacheLimit(n), unpair: () => manager.remove(account.deviceId) },
    [raw],
  );

  if (!account || !session || account.status === "revoked") {
    return <PairScreen revoked={account?.status === "revoked"} onPair={(c, n) => manager.pair(c, n)} />;
  }
  const state = account.state;
  if (state.status === "pending") {
    return <PendingScreen session={session} state={state} onCancelled={() => undefined} />;
  }
  return <ChatView session={session} state={state} onUnpaired={() => undefined} />;
}
