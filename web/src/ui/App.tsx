import { useEffect, useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import type { Session } from "../core/session";
import { AccountStrip } from "./AccountStrip";
import { ChatView } from "./ChatView";
import { PairScreen } from "./PairScreen";
import { PendingScreen } from "./PendingScreen";
import { RevokedNotice } from "./RevokedNotice";
import { initialSelection, validSelection, type Selection } from "./selection";

const TAB_KEY = "chatterror.account";

function storedTab(): string | null {
  try {
    return localStorage.getItem(TAB_KEY);
  } catch {
    return null;
  }
}

function storeTab(selection: Selection) {
  try {
    if (selection !== "add") localStorage.setItem(TAB_KEY, selection);
  } catch {
    // Private mode or blocked storage; the tab is just not remembered.
  }
}

// Cache limit and unpair go through the manager so the registry stays in sync.
// Cached so ChatView gets a stable session and doesn't reload its history.
const managedSessions = new WeakMap<Session, Session>();

function managedSession(manager: AccountManager, deviceId: string): Session {
  const raw = manager.session(deviceId)!;
  let session = managedSessions.get(raw);
  if (!session) {
    session = { ...raw, setCacheLimit: (n) => manager.setCacheLimit(n), unpair: () => manager.remove(deviceId) };
    managedSessions.set(raw, session);
  }
  return session;
}

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
  const [selected, setSelected] = useState<Selection>(() => initialSelection(manager.list(), location.hash, storedTab()));
  const current = validSelection(selected, accounts);

  useEffect(() => {
    if (location.hash.startsWith("#account=")) history.replaceState(null, "", location.pathname + location.search);
  }, []);

  useEffect(() => {
    manager.setViewing(current === "add" ? null : current);
    storeTab(current);
  }, [manager, current]);

  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    const onMessage = (event: MessageEvent) => {
      if (event.data?.type === "openAccount" && typeof event.data.deviceId === "string") setSelected(event.data.deviceId);
    };
    navigator.serviceWorker.addEventListener("message", onMessage);
    return () => navigator.serviceWorker.removeEventListener("message", onMessage);
  }, []);

  async function pair(code: string, name: string) {
    const { deviceId } = await manager.pair(code, name);
    setSelected(deviceId);
  }

  function renderAccount(account: AccountView) {
    if (account.status === "revoked") {
      return <RevokedNotice label={account.label} onRemove={() => manager.remove(account.deviceId)} onPairAgain={() => setSelected("add")} />;
    }
    if (account.state.status === "pending") {
      return <PendingScreen state={account.state} onCancel={() => manager.remove(account.deviceId)} />;
    }
    return (
      <ChatView
        key={account.deviceId}
        session={managedSession(manager, account.deviceId)}
        state={account.state}
        onUnpaired={() => void manager.remove(account.deviceId)}
        onAddAccount={() => setSelected("add")}
      />
    );
  }

  if (accounts.length === 0) return <PairScreen onPair={pair} />;

  return (
    <div class="app">
      {accounts.length > 1 && <AccountStrip accounts={accounts} selected={current} onSelect={setSelected} />}
      {current === "add" ? (
        <PairScreen onPair={pair} onBack={() => setSelected(accounts[0].deviceId)} />
      ) : current === "all" ? (
        // No merged feed yet, so All shows the first account.
        renderAccount(accounts[0])
      ) : (
        renderAccount(accounts.find((a) => a.deviceId === current)!)
      )}
    </div>
  );
}
