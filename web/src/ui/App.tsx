import { useEffect, useMemo, useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import { channelIncludes, customKey, EMPTY_CHANNEL_PREFS, isItemMuted, saveCustom, withTells, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import { AccountSettings } from "./AccountSettings";
import { AppSettings } from "./AppSettings";
import { ChannelList } from "./ChannelList";
import { ChannelMenu, type MenuTarget } from "./ChannelMenu";
import { ChannelRail } from "./ChannelRail";
import { CharacterBar } from "./CharacterBar";
import { characterGroups, describeCharacter } from "./characters";
import { CharacterSheet } from "./CharacterSheet";
import { charactersOf, partnersOf, railChannels, subRows, type SubRow } from "./channels";
import { ChatPane } from "./ChatPane";
import { CustomChannelSheet } from "./CustomChannelSheet";
import { Drawer } from "./Drawer";
import { MenuIcon } from "./icons";
import { PairScreen } from "./PairScreen";
import { createPendingSends, type PendingSend, type PendingSends } from "./pending";
import { PendingScreen } from "./PendingScreen";
import { chatTarget, initialPlace, parseLink, parseViews, pickTarget, PLACE_KEY, remember, resolveChannel, resolveRow, SERVER_KEY, SUB_KEY, validPlace, viewKey, type Picks, type Place } from "./place";
import { RevokedNotice } from "./RevokedNotice";
import { characterUnread, createUnreadTracker, viewUnread, type UnreadTracker } from "./unread";
import { useFeed } from "./useFeed";

const OLD_KEYS = ["chatterror.nav", "chatterror.lastChannels", "chatterror.account"];

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

function forgetOldKeys() {
  try {
    for (const key of OLD_KEYS) localStorage.removeItem(key);
    for (const key of Object.keys(localStorage)) if (key.startsWith("chatterror.toggled.")) localStorage.removeItem(key);
  } catch {
    // Blocked storage; the old keys are never read again anyway.
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
    createUnreadTracker(manager, (deviceId, key) => {
      const prefs = manager.session(deviceId)?.getState().channelPrefs;
      return !!prefs && isItemMuted(prefs, key);
    }),
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

// A notification link also picks the channel and row the message shows in.
function initialPicks(accounts: AccountView[]): Picks {
  const picks = { server: parseViews(stored(SERVER_KEY)), sub: parseViews(stored(SUB_KEY)) };
  const link = parseLink(location.hash);
  const account = link && accounts.find((a) => a.deviceId === link.deviceId);
  const target = account && link.chat ? chatTarget(account.state.channelPrefs, link.chat) : null;
  return account && target ? pickTarget(picks, account.deviceId, target) : picks;
}

export function App({ manager }: { manager: AccountManager }) {
  const accounts = useAccounts(manager);
  const [sends] = useState(() => createPendingSends(manager));
  const pending = usePendingSends(sends);
  const unread = useUnreadTracker(manager);
  const [saved, setPlace] = useState(() => initialPlace(manager.list(), location.hash, stored(PLACE_KEY), charactersOf));
  const [picks, setPicks] = useState(() => initialPicks(manager.list()));
  const [adding, setAdding] = useState(() => location.hash.startsWith("#pair="));
  const [pairingAgain, setPairingAgain] = useState(false);
  const [notified, setNotified] = useState(0);
  const place = validPlace(saved, accounts, charactersOf);

  useEffect(() => {
    if (location.hash.startsWith("#account=")) history.replaceState(null, "", location.pathname + location.search);
    forgetOldKeys();
  }, []);

  useEffect(() => {
    manager.setViewing(adding || !place ? null : place.deviceId);
  }, [manager, adding, place?.deviceId]);

  useEffect(() => {
    if (place) store(PLACE_KEY, JSON.stringify(place));
  }, [place?.deviceId, place?.character]);

  useEffect(() => store(SERVER_KEY, JSON.stringify(picks.server)), [picks.server]);
  useEffect(() => store(SUB_KEY, JSON.stringify(picks.sub)), [picks.sub]);

  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    const onMessage = (event: MessageEvent) => {
      if (event.data?.type !== "openAccount" || typeof event.data.deviceId !== "string") return;
      openChat(event.data.deviceId, typeof event.data.chat === "string" ? event.data.chat : null);
    };
    navigator.serviceWorker.addEventListener("message", onMessage);
    return () => navigator.serviceWorker.removeEventListener("message", onMessage);
  }, []);

  function openChat(deviceId: string, chat: string | null) {
    const account = manager.list().find((a) => a.deviceId === deviceId);
    if (!account) return;
    const target = chat ? chatTarget(account.state.channelPrefs, chat) : null;
    if (target) setPicks((p) => pickTarget(p, deviceId, target));
    setPlace((current) => ({ deviceId, character: target?.character ?? (current?.deviceId === deviceId ? current.character : null) }));
    setAdding(false);
    setNotified((n) => n + 1);
  }

  async function pair(code: string, name: string) {
    const { deviceId } = await manager.pair(code, name);
    setPlace({ deviceId, character: null });
    setAdding(false);
    setPairingAgain(false);
  }

  if (!place) return <PairScreen onPair={pair} />;
  if (adding) {
    return (
      <PairScreen
        pairAgain={pairingAgain}
        onPair={pair}
        onBack={() => {
          setAdding(false);
          setPairingAgain(false);
        }}
      />
    );
  }

  return (
    <Workspace
      manager={manager}
      accounts={accounts}
      place={place}
      setPlace={setPlace}
      picks={picks}
      setPicks={setPicks}
      sends={sends}
      pending={pending}
      unread={unread}
      notified={notified}
      onAdd={() => setAdding(true)}
      onPairAgain={() => {
        setPairingAgain(true);
        setAdding(true);
      }}
    />
  );
}

type Settings = { app: true } | { deviceId: string };

function Workspace({ manager, accounts, place, setPlace, picks, setPicks, sends, pending, unread, notified, onAdd, onPairAgain }: {
  manager: AccountManager;
  accounts: AccountView[];
  place: Place;
  setPlace: (place: Place) => void;
  picks: Picks;
  setPicks: (update: (picks: Picks) => Picks) => void;
  sends: PendingSends;
  pending: PendingSend[];
  unread: UnreadTracker;
  notified: number;
  onAdd: () => void;
  onPairAgain: () => void;
}) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [settings, setSettings] = useState<Settings | null>(null);
  const [switching, setSwitching] = useState(false);
  const [menu, setMenu] = useState<MenuTarget | null>(null);
  const [editing, setEditing] = useState<{ custom?: CustomChannel } | null>(null);
  const [epoch, setEpoch] = useState(0);

  const account = accounts.find((a) => a.deviceId === place.deviceId)!;
  const deviceId = account.deviceId;
  const usable = account.status === "active" && account.state.status !== "pending";
  const character = usable ? place.character : null;
  const { items, loaded } = useFeed(manager, deviceId, epoch);
  const storedPrefs = account.state.channelPrefs;
  const prefs = useMemo(() => (character ? withTells(storedPrefs, character) : storedPrefs), [storedPrefs, character]);
  const save = (next: ChannelPrefs) => void manager.session(deviceId)?.setChannelPrefs(next).catch(() => undefined);

  // Saved once so muting or notifying Tells sticks; the view already uses it either way.
  useEffect(() => {
    if (prefs !== storedPrefs) save(prefs);
  }, [prefs, storedPrefs]);

  // A client can start with only messages from its history to tell which characters it has.
  const known = useMemo(() => (usable ? charactersOf(account, items) : []), [account, items, usable]);
  useEffect(() => {
    if (usable && !place.character && known[0]) setPlace({ deviceId, character: known[0] });
  }, [usable, place.character, known[0]]);

  const rail = useMemo(() => (character ? railChannels(prefs, character) : []), [prefs, character]);
  const custom = character ? resolveChannel(rail, picks.server[viewKey(deviceId, character)]) : null;
  const partners = useMemo(() => (character ? partnersOf(items, character, prefs.pinned) : []), [items, character, prefs.pinned]);
  const rows = useMemo(() => (custom ? subRows(custom, partners) : []), [custom, partners]);
  const row = custom ? resolveRow(rows, picks.sub[viewKey(deviceId, customKey(custom))]) : null;

  // Before history loads the row can resolve to a fallback, which must not be cleared.
  const shownKey = loaded && row ? row.key : null;
  const includes = useMemo(() => (shownKey ? (key: string) => channelIncludes(prefs, shownKey, key) : null), [shownKey, prefs]);
  const unreadCount = useMemo(() => (includes ? unread.count(deviceId, includes) : 0), [deviceId, includes]);

  const covered = !!settings || switching || !!menu || !!editing;
  const watching = !drawerOpen && !covered;

  useEffect(() => {
    unread.setOpen(includes && watching ? { deviceId, includes } : null);
  }, [unread, deviceId, includes, watching]);

  useEffect(() => () => unread.setOpen(null), [unread]);

  // A notification tap lands in the chat, not behind a drawer or sheet.
  useEffect(() => {
    setDrawerOpen(false);
    setSettings(null);
    setSwitching(false);
    setMenu(null);
    setEditing(null);
  }, [notified]);

  function selectChannel(next: CustomChannel) {
    if (!character) return;
    setPicks((p) => ({ ...p, server: remember(p.server, viewKey(deviceId, character), next.id) }));
    if (subRows(next, partners).length === 1) setDrawerOpen(false);
  }

  function openRow(next: SubRow) {
    if (!custom) return;
    setPicks((p) => ({ ...p, sub: remember(p.sub, viewKey(deviceId, customKey(custom)), next.key) }));
    setDrawerOpen(false);
  }

  // A removed client has no settings left; its place shows Remove and Pair again instead.
  function openClientSettings(id: string) {
    if (accounts.find((a) => a.deviceId === id)?.status === "revoked") {
      setSettings(null);
      setPlace({ deviceId: id, character: null });
      return;
    }
    setSettings({ deviceId: id });
  }

  const channelMenu = (c: CustomChannel): MenuTarget => ({ kind: "channel", custom: c, keys: rail.map(customKey) });
  const me = describeCharacter(account, character);
  const otherTells = unread.total(accounts.map((a) => a.deviceId)).tells - (character ? unread.summary(deviceId, character).tells : 0);

  function sidebar() {
    if (account.status === "revoked") return <RevokedNotice inline label={account.label} onRemove={() => manager.remove(deviceId)} onPairAgain={onPairAgain} />;
    if (account.state.status === "pending") return <PendingScreen inline state={account.state} onCancel={() => manager.remove(deviceId)} />;
    if (!custom || !character) return <p class="hint channels-empty">No characters yet. Log in to a character with the plugin running.</p>;
    return (
      <ChannelList
        custom={custom}
        character={character}
        rows={rows}
        selected={row?.key ?? null}
        prefs={prefs}
        unreadOf={(r) => viewUnread(unread, deviceId, prefs, r.key).count}
        onOpen={openRow}
        onMenu={() => setMenu(channelMenu(custom))}
        onRowMenu={(r) => r.partner && setMenu({ kind: "partner", key: r.key, partner: r.partner })}
      />
    );
  }

  const showChat = account.status !== "revoked" && account.state.status !== "pending";

  function chat() {
    if (account.status === "revoked") return <RevokedNotice label={account.label} onRemove={() => manager.remove(deviceId)} onPairAgain={onPairAgain} />;
    if (account.state.status === "pending") return <PendingScreen state={account.state} onCancel={() => manager.remove(deviceId)} />;
    return (
      <ChatPane
        key={`${deviceId}-${epoch}`}
        account={account}
        prefs={prefs}
        custom={custom}
        row={row}
        items={items}
        unreadCount={unreadCount}
        sends={sends}
        pending={pending}
        onMenu={() => setDrawerOpen(true)}
      />
    );
  }

  function settingsSheet() {
    if (!settings) return null;
    if (!("deviceId" in settings)) {
      return <AppSettings manager={manager} accounts={accounts} onClose={() => setSettings(null)} onOpenAccount={openClientSettings} onAddAccount={onAdd} />;
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
        onClose={() => setSettings({ app: true })}
        onCacheCleared={() => setEpoch((e) => e + 1)}
      />
    );
  }

  const drawer = (
    <div class="drawer-content">
      <div class="drawer-main">
        <ChannelRail
          rail={rail}
          selected={custom?.id ?? null}
          prefs={prefs}
          unreadOf={(c) => viewUnread(unread, deviceId, prefs, customKey(c))}
          onSelect={selectChannel}
          onMenu={(c) => setMenu(channelMenu(c))}
          onAdd={() => setEditing({})}
        />
        <div class="sidebar">{sidebar()}</div>
      </div>
      <CharacterBar
        name={me.name}
        world={me.world}
        status={me.status}
        online={me.online}
        otherTells={otherTells}
        onSwitch={() => setSwitching(true)}
        onSettings={() => {
          setDrawerOpen(false);
          setSettings({ app: true });
        }}
      />
    </div>
  );

  // Sheets sit outside the drawer so its inert never covers them.
  return (
    <>
      <Drawer open={drawerOpen} onOpenChange={setDrawerOpen} swipe={!covered} drawer={drawer}>
        {!showChat && (
          <button class="icon-btn menu-btn floating" aria-label="Open navigation" onClick={() => setDrawerOpen(true)}>
            <MenuIcon />
          </button>
        )}
        {chat()}
      </Drawer>
      {settingsSheet()}
      {switching && (
        <CharacterSheet
          groups={characterGroups(accounts, (a) => charactersOf(a, a.deviceId === deviceId ? items : []), (id, name) => characterUnread(unread, id, manager.session(id)?.getState().channelPrefs ?? EMPTY_CHANNEL_PREFS, name))}
          current={place}
          onPick={(next) => {
            setSwitching(false);
            setPlace(next);
          }}
          onPair={() => {
            setSwitching(false);
            onAdd();
          }}
          onClose={() => setSwitching(false)}
        />
      )}
      {menu && <ChannelMenu target={menu} prefs={prefs} onChange={save} onEdit={(c) => setEditing({ custom: c })} onClose={() => setMenu(null)} />}
      {editing && character && (
        <CustomChannelSheet
          character={character}
          custom={editing.custom}
          relayChannels={account.state.relayChannels}
          onSave={(c) => {
            save(saveCustom(prefs, c));
            if (!editing.custom) setPicks((p) => ({ ...p, server: remember(p.server, viewKey(deviceId, character), c.id) }));
          }}
          onClose={() => setEditing(null)}
        />
      )}
    </>
  );
}
