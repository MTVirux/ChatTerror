import { useEffect, useMemo, useRef, useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import { AccountSettings } from "./AccountSettings";
import { AppSettings } from "./AppSettings";
import { buildChannelTree, channelKey, type ChannelRef } from "./channels";
import { ChannelList, HomeList } from "./ChannelList";
import { ChatPane } from "./ChatPane";
import { Drawer } from "./Drawer";
import { GearIcon, MenuIcon } from "./icons";
import { initialNav, legacyNav, navTo, parseLastChannels, rememberChannel, resolveChannel, serializeNav, validNav, type Nav, type Server } from "./nav";
import { PairScreen } from "./PairScreen";
import { loadShowEmpty, saveShowEmpty } from "./prefs";
import { createPendingSends, type PendingSend, type PendingSends } from "./pending";
import { PendingScreen } from "./PendingScreen";
import { RevokedNotice } from "./RevokedNotice";
import { ServerRail } from "./ServerRail";
import { createUnreadTracker, type UnreadTracker } from "./unread";
import { useFeed } from "./useFeed";

const NAV_KEY = "chatterror.nav";
const LAST_KEY = "chatterror.lastChannels";
const LEGACY_KEY = "chatterror.account";

function stored(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function store(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
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
  const [tracker] = useState(() =>
    createUnreadTracker(manager, (deviceId, key) => manager.session(deviceId)?.getState().channelPrefs.muted.includes(key) ?? false),
  );
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

type Settings = { app: true } | { deviceId: string; fromApp?: boolean };

export function App({ manager }: { manager: AccountManager }) {
  const accounts = useAccounts(manager);
  const [sends] = useState(() => createPendingSends(manager));
  const pending = usePendingSends(sends);
  const unread = useUnreadTracker(manager);
  const [last] = useState(() => ({ current: parseLastChannels(stored(LAST_KEY)) }));
  const [saved, setNav] = useState<Nav>(() => initialNav(manager.list(), location.hash, stored(NAV_KEY) ?? legacyNav(stored(LEGACY_KEY)), last.current));
  const nav = validNav(saved, accounts);
  const go = (server: Server) => setNav(navTo(server, last.current));
  const [pairingAgain, setPairingAgain] = useState(false);
  const [notified, setNotified] = useState(0);
  const beforeAdd = useRef<Server | null>(null);

  useEffect(() => {
    if (location.hash.startsWith("#account=")) history.replaceState(null, "", location.pathname + location.search);
    try {
      localStorage.removeItem(LEGACY_KEY);
    } catch {
      // Blocked storage; the old key is simply ignored once the new one exists.
    }
  }, []);

  useEffect(() => {
    manager.setViewing(nav.server === "add" ? null : nav.server === "home" ? "all" : nav.server);
    if (nav.server === "add") return;
    setPairingAgain(false);
    beforeAdd.current = nav.server;
  }, [manager, nav.server]);

  useEffect(() => {
    if (nav.server !== "add") store(NAV_KEY, serializeNav(nav));
    const next = rememberChannel(last.current, nav);
    if (next !== last.current) {
      last.current = next;
      store(LAST_KEY, JSON.stringify(next));
    }
  }, [nav.server, nav.channel]);

  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    const onMessage = (event: MessageEvent) => {
      if (event.data?.type !== "openAccount" || typeof event.data.deviceId !== "string") return;
      go(event.data.deviceId);
      setNotified((n) => n + 1);
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
    return <PairScreen pairAgain={pairingAgain} onPair={pair} onBack={() => go(beforeAdd.current ?? accounts[0].deviceId)} />;
  }

  return (
    <Workspace
      manager={manager}
      accounts={accounts}
      nav={nav}
      setNav={setNav}
      go={go}
      sends={sends}
      pending={pending}
      unread={unread}
      notified={notified}
      onPairAgain={() => {
        setPairingAgain(true);
        setNav({ server: "add", channel: null });
      }}
    />
  );
}

function Workspace({ manager, accounts, nav, setNav, go, sends, pending, unread, notified, onPairAgain }: {
  manager: AccountManager;
  accounts: AccountView[];
  nav: Nav;
  setNav: (nav: Nav) => void;
  go: (server: Server) => void;
  sends: PendingSends;
  pending: PendingSend[];
  unread: UnreadTracker;
  notified: number;
  onPairAgain: () => void;
}) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [settings, setSettings] = useState<Settings | null>(null);
  const [epoch, setEpoch] = useState(0);
  const [showEmpty, setShowEmpty] = useState(loadShowEmpty);
  const home = nav.server === "home";
  const account = home ? undefined : accounts.find((a) => a.deviceId === nav.server);
  const { items, loaded } = useFeed(manager, home ? null : nav.server, epoch);

  const tree = useMemo(
    () => (account ? buildChannelTree(items, { character: account.character, relayChannels: account.state.relayChannels, showEmpty }) : []),
    [items, account?.character, account?.state.relayChannels, showEmpty],
  );
  const channel = home ? null : resolveChannel(nav.channel, tree);
  const openKey = channel && channelKey(channel);

  // Before history loads the channel can resolve to a fallback, which must not be cleared or pinned.
  const shownKey = loaded ? openKey : null;
  const unreadCount = useMemo(() => (home || !shownKey ? 0 : unread.count(nav.server, shownKey)), [home, nav.server, shownKey]);

  useEffect(() => {
    unread.setOpen(home ? "home" : shownKey ? { deviceId: nav.server, key: shownKey } : null);
  }, [unread, home, nav.server, shownKey]);

  // Pin the resolved channel so new messages don't move the view.
  useEffect(() => {
    if (!home && shownKey && shownKey !== nav.channel) setNav({ server: nav.server, channel: shownKey });
  }, [home, nav.server, shownKey, nav.channel]);

  useEffect(() => () => unread.setOpen(null), [unread]);

  // A notification tap lands in the chat, not behind a drawer or sheet.
  useEffect(() => {
    setDrawerOpen(false);
    setSettings(null);
  }, [notified]);

  function selectServer(server: Server) {
    if (server !== nav.server) go(server);
  }

  function openChannel(ref: ChannelRef) {
    setNav({ server: nav.server, channel: channelKey(ref) });
    setDrawerOpen(false);
    setSettings(null);
  }

  function openSettings(next: Settings) {
    setDrawerOpen(false);
    setSettings(next);
  }

  // A removed account has no settings left, its channel list offers Remove and Pair again instead.
  function openAccountFromApp(deviceId: string) {
    selectServer(deviceId);
    const target = accounts.find((a) => a.deviceId === deviceId);
    setSettings(target?.status === "revoked" ? null : { deviceId, fromApp: true });
  }

  function settingsSheet() {
    if (!settings) return null;
    if (!("deviceId" in settings)) {
      return (
        <AppSettings
          manager={manager}
          accounts={accounts}
          onClose={() => setSettings(null)}
          onOpenAccount={openAccountFromApp}
          onAddAccount={() => go("add")}
          showEmpty={showEmpty}
          onShowEmptyChange={(show) => { setShowEmpty(show); saveShowEmpty(show); }}
        />
      );
    }
    const target = accounts.find((a) => a.deviceId === settings.deviceId);
    const session = target && manager.session(target.deviceId);
    if (!target || !session) return null;
    return (
      <AccountSettings
        key={target.deviceId}
        manager={manager}
        account={target}
        session={session}
        onClose={() => setSettings(settings.fromApp ? { app: true } : null)}
        onCacheCleared={() => setEpoch((e) => e + 1)}
      />
    );
  }

  const chatPane = account?.status !== "revoked" && account?.state.status !== "pending";

  function chat() {
    if (account?.status === "revoked") {
      return <RevokedNotice label={account.label} onRemove={() => manager.remove(account.deviceId)} onPairAgain={onPairAgain} />;
    }
    if (account?.state.status === "pending") {
      return <PendingScreen state={account.state} onCancel={() => manager.remove(account.deviceId)} />;
    }
    return (
      <ChatPane
        key={`${nav.server}-${epoch}`}
        accounts={accounts}
        server={nav.server}
        channel={channel}
        items={items}
        unreadCount={unreadCount}
        sends={sends}
        pending={pending}
        onMenu={() => setDrawerOpen(true)}
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
            tree={tree}
            unread={unread}
            channel={channel}
            onOpenChannel={openChannel}
            onOpenSettings={() => openSettings({ deviceId: account.deviceId })}
            onPairAgain={onPairAgain}
          />
        ) : (
          <HomeList accounts={accounts} unread={unread} onSelectServer={selectServer} />
        )}
        <div class="sidebar-foot">
          <button class="icon-btn" aria-label="App settings" onClick={() => openSettings({ app: true })}>
            <GearIcon />
          </button>
        </div>
      </div>
    </div>
  );

  // The sheet sits outside the drawer so its inert never covers it.
  return (
    <>
      <Drawer open={drawerOpen} onOpenChange={setDrawerOpen} swipe={!settings} drawer={drawer}>
        {!chatPane && (
          <button class="icon-btn menu-btn floating" aria-label="Open navigation" onClick={() => setDrawerOpen(true)}>
            <MenuIcon />
          </button>
        )}
        {chat()}
      </Drawer>
      {settingsSheet()}
    </>
  );
}
