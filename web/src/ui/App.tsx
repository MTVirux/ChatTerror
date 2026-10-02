import { useEffect, useMemo, useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import { buildChannelTree, channelKey, type ChannelRef } from "./channels";
import { ChannelList, HomeList } from "./ChannelList";
import { ChatView } from "./ChatView";
import { Drawer } from "./Drawer";
import { GearIcon, MenuIcon } from "./icons";
import { initialNav, resolveChannel, serializeNav, validNav, type Nav, type Server } from "./nav";
import { PairScreen } from "./PairScreen";
import { createPendingSends, type PendingSend, type PendingSends } from "./pending";
import { PendingScreen } from "./PendingScreen";
import { RevokedNotice } from "./RevokedNotice";
import { ServerRail } from "./ServerRail";
import { SettingsView } from "./SettingsView";
import { createUnreadTracker, type UnreadTracker } from "./unread";
import { useFeed } from "./useFeed";

const NAV_KEY = "chatterror.nav";

function storedNav(): string | null {
  try {
    return localStorage.getItem(NAV_KEY);
  } catch {
    return null;
  }
}

function storeNav(nav: Nav) {
  try {
    if (nav.server !== "add") localStorage.setItem(NAV_KEY, serializeNav(nav));
  } catch {
    // Private mode or blocked storage; the place is just not remembered.
  }
}

function useAccounts(manager: AccountManager): AccountView[] {
  const [accounts, setAccounts] = useState(manager.list());
  useEffect(() => {
    setAccounts(manager.list());
    return manager.subscribe(setAccounts);
  }, [manager]);
  return accounts;
}

function usePendingSends(sends: PendingSends): PendingSend[] {
  const [list, setList] = useState(sends.list());
  useEffect(() => sends.subscribe(setList), [sends]);
  return list;
}

function useUnreadTracker(manager: AccountManager): UnreadTracker {
  const [tracker] = useState(() => createUnreadTracker(manager));
  const [, setVersion] = useState(0);
  useEffect(() => {
    const off = tracker.subscribe(() => setVersion((v) => v + 1));
    return () => {
      off();
      tracker.close();
    };
  }, [tracker]);
  return tracker;
}

export function App({ manager }: { manager: AccountManager }) {
  const accounts = useAccounts(manager);
  const [sends] = useState(() => createPendingSends(manager));
  const pending = usePendingSends(sends);
  const unread = useUnreadTracker(manager);
  const [saved, setNav] = useState<Nav>(() => initialNav(manager.list(), location.hash, storedNav()));
  const nav = validNav(saved, accounts);
  const [pairingAgain, setPairingAgain] = useState(false);

  useEffect(() => {
    if (location.hash.startsWith("#account=")) history.replaceState(null, "", location.pathname + location.search);
  }, []);

  useEffect(() => {
    manager.setViewing(nav.server === "add" ? null : nav.server === "home" ? "all" : nav.server);
    if (nav.server !== "add") setPairingAgain(false);
  }, [manager, nav.server]);

  useEffect(() => storeNav(nav), [nav.server, nav.channel]);

  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    const onMessage = (event: MessageEvent) => {
      if (event.data?.type === "openAccount" && typeof event.data.deviceId === "string") setNav({ server: event.data.deviceId, channel: null });
    };
    navigator.serviceWorker.addEventListener("message", onMessage);
    return () => navigator.serviceWorker.removeEventListener("message", onMessage);
  }, []);

  async function pair(code: string, name: string) {
    const { deviceId } = await manager.pair(code, name);
    setNav({ server: deviceId, channel: null });
  }

  if (accounts.length === 0) return <PairScreen onPair={pair} />;
  if (nav.server === "add") {
    return <PairScreen pairAgain={pairingAgain} onPair={pair} onBack={() => setNav({ server: accounts[0].deviceId, channel: null })} />;
  }

  return (
    <Workspace
      manager={manager}
      accounts={accounts}
      nav={nav}
      setNav={setNav}
      sends={sends}
      pending={pending}
      unread={unread}
      onPairAgain={() => {
        setPairingAgain(true);
        setNav({ server: "add", channel: null });
      }}
    />
  );
}

function Workspace({ manager, accounts, nav, setNav, sends, pending, unread, onPairAgain }: {
  manager: AccountManager;
  accounts: AccountView[];
  nav: Nav;
  setNav: (nav: Nav) => void;
  sends: PendingSends;
  pending: PendingSend[];
  unread: UnreadTracker;
  onPairAgain: () => void;
}) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [settings, setSettings] = useState<{ accountId: string | null } | null>(null);
  const [epoch, setEpoch] = useState(0);
  const home = nav.server === "home";
  const account = home ? undefined : accounts.find((a) => a.deviceId === nav.server);
  const items = useFeed(manager, home ? null : nav.server, epoch);

  const tree = useMemo(
    () => (account ? buildChannelTree(items, { character: account.character, relayChannels: account.state.relayChannels }) : []),
    [items, account?.character, account?.state.relayChannels],
  );
  const channel = home ? null : resolveChannel(nav.channel, tree);
  const openKey = channel && channelKey(channel);

  useEffect(() => {
    unread.setOpen(home ? "home" : openKey ? { deviceId: nav.server, key: openKey } : null);
  }, [unread, home, nav.server, openKey]);

  useEffect(() => () => unread.setOpen(null), [unread]);

  function selectServer(server: Server) {
    if (server !== nav.server) setNav({ server, channel: null });
  }

  function openChannel(ref: ChannelRef) {
    setNav({ server: nav.server, channel: channelKey(ref) });
    setDrawerOpen(false);
  }

  function openSettings(accountId: string | null) {
    setDrawerOpen(false);
    setSettings({ accountId });
  }

  function chat() {
    if (account?.status === "revoked") {
      return <RevokedNotice label={account.label} onRemove={() => manager.remove(account.deviceId)} onPairAgain={onPairAgain} />;
    }
    if (account?.state.status === "pending") {
      return <PendingScreen state={account.state} onCancel={() => manager.remove(account.deviceId)} />;
    }
    return (
      <ChatView
        key={`${nav.server}-${epoch}`}
        manager={manager}
        accounts={accounts}
        accountId={home ? null : nav.server}
        channel={channel}
        sends={sends}
        pending={pending}
        onOpenAccount={(deviceId) => setNav({ server: deviceId, channel: null })}
        onAddAccount={() => setNav({ server: "add", channel: null })}
      />
    );
  }

  const drawer = (
    <div class="drawer-content">
      <ServerRail accounts={accounts} server={nav.server} unread={unread} onSelect={selectServer} />
      <div class="sidebar">
        {account ? (
          <ChannelList
            key={account.deviceId}
            manager={manager}
            account={account}
            items={items}
            unread={unread}
            channel={channel}
            onOpenChannel={openChannel}
            onOpenSettings={() => openSettings(account.deviceId)}
            onPairAgain={onPairAgain}
          />
        ) : (
          <HomeList accounts={accounts} unread={unread} onSelectServer={selectServer} />
        )}
        <div class="sidebar-foot">
          <button class="icon-btn" aria-label="App settings" onClick={() => openSettings(null)}>
            <GearIcon />
          </button>
        </div>
      </div>
    </div>
  );

  return (
    <Drawer open={drawerOpen} onOpenChange={setDrawerOpen} drawer={drawer}>
      <button class="icon-btn menu-btn" aria-label="Open navigation" aria-expanded={drawerOpen} onClick={() => setDrawerOpen(true)}>
        <MenuIcon />
      </button>
      {chat()}
      {settings && (
        <SettingsView
          manager={manager}
          accounts={accounts}
          accountId={settings.accountId}
          onClose={() => setSettings(null)}
          onCacheCleared={() => setEpoch((e) => e + 1)}
          onOpenAccount={(deviceId) => setNav({ server: deviceId, channel: null })}
          onAddAccount={() => setNav({ server: "add", channel: null })}
        />
      )}
    </Drawer>
  );
}
