import { useEffect, useMemo, useRef, useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import { channelIncludes, customKey, EMPTY_CHANNEL_PREFS, isItemMuted, saveCustom, type ChannelPrefs, type CustomChannel } from "../core/channelPrefs";
import { AccountSettings } from "./AccountSettings";
import { AppSettings } from "./AppSettings";
import { useBackClose } from "./backStack";
import { ChannelMenu, type MenuTarget } from "./ChannelMenu";
import { characterGroups, describeCharacter } from "./characters";
import { CharacterSheet } from "./CharacterSheet";
import { charactersOf, partnersOf } from "./channels";
import { chatForKey, chatRows, findChat, type ChatRow } from "./chatRows";
import { ChatList } from "./ChatList";
import { ChatPane } from "./ChatPane";
import { GroupEditor } from "./GroupEditor";
import { NewChat } from "./NewChat";
import { PairScreen } from "./PairScreen";
import { createPendingSends, type PendingSend, type PendingSends } from "./pending";
import { PendingScreen } from "./PendingScreen";
import { newChatPeople } from "./people";
import { chatTarget, initialPlace, LAST_CHAT_KEY, parseLink, parseViews, PLACE_KEY, remember, validPlace, viewKey, type Place } from "./place";
import { RevokedNotice } from "./RevokedNotice";
import { characterUnread, createUnreadTracker, rowUnread, type UnreadTracker } from "./unread";
import { useFeed } from "./useFeed";
import { useMedia, WIDE_QUERY } from "./useMedia";

const OLD_KEYS = ["chatterror.nav", "chatterror.lastChannels", "chatterror.account", "chatterror.lastServer", "chatterror.lastSub"];

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

// The message key a notification link was about, opened once its client's history loads.
function linkedChat(accounts: AccountView[]): string | null {
  const link = parseLink(location.hash);
  if (!link?.chat || !accounts.some((a) => a.deviceId === link.deviceId)) return null;
  return chatTarget(link.chat)?.chat ?? null;
}

export function App({ manager }: { manager: AccountManager }) {
  const accounts = useAccounts(manager);
  const [sends] = useState(() => createPendingSends(manager));
  const pending = usePendingSends(sends);
  const unread = useUnreadTracker(manager);
  const [saved, setPlace] = useState(() => initialPlace(manager.list(), location.hash, stored(PLACE_KEY), charactersOf));
  const [awaited] = useState(() => ({ current: linkedChat(manager.list()) }));
  const [adding, setAdding] = useState(() => location.hash.startsWith("#pair="));
  const [pairingAgain, setPairingAgain] = useState(false);
  const [notified, setNotified] = useState(0);
  const place = validPlace(saved, accounts, charactersOf);

  function closePairing() {
    setAdding(false);
    setPairingAgain(false);
  }

  useBackClose(closePairing, adding && !!place);

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
    if (!manager.list().some((a) => a.deviceId === deviceId)) return;
    const target = chat ? chatTarget(chat) : null;
    awaited.current = target?.chat ?? null;
    setPlace((current) => ({ deviceId, character: target?.character ?? (current?.deviceId === deviceId ? current.character : null) }));
    setAdding(false);
    setPairingAgain(false);
    setNotified((n) => n + 1);
  }

  async function pair(code: string, name: string) {
    const { deviceId } = await manager.pair(code, name);
    setPlace({ deviceId, character: null });
    setAdding(false);
    setPairingAgain(false);
  }

  if (!place) return <PairScreen onPair={pair} />;
  if (adding) return <PairScreen pairAgain={pairingAgain} onPair={pair} onBack={closePairing} />;

  return (
    <Workspace
      manager={manager}
      accounts={accounts}
      place={place}
      setPlace={setPlace}
      awaited={awaited}
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

function Workspace({ manager, accounts, place, setPlace, awaited, sends, pending, unread, notified, onAdd, onPairAgain }: {
  manager: AccountManager;
  accounts: AccountView[];
  place: Place;
  setPlace: (place: Place) => void;
  awaited: { current: string | null };
  sends: PendingSends;
  pending: PendingSend[];
  unread: UnreadTracker;
  notified: number;
  onAdd: () => void;
  onPairAgain: () => void;
}) {
  const wide = useMedia(WIDE_QUERY);
  const [opened, setOpened] = useState<{ place: string; key: string | null } | null>(null);
  const [lastChat, setLastChat] = useState(() => parseViews(stored(LAST_CHAT_KEY)));
  const [newChat, setNewChat] = useState(false);
  const [editing, setEditing] = useState<{ custom?: CustomChannel } | null>(null);
  const [menu, setMenu] = useState<MenuTarget | null>(null);
  const [settings, setSettings] = useState<Settings | null>(null);
  const [switching, setSwitching] = useState(false);
  const [epoch, setEpoch] = useState(0);

  const account = accounts.find((a) => a.deviceId === place.deviceId)!;
  const deviceId = account.deviceId;
  const usable = account.status === "active" && account.state.status !== "pending";
  const character = usable ? place.character : null;
  const { items, loaded } = useFeed(manager, deviceId, epoch);
  const prefs = account.state.channelPrefs;
  const save = (next: ChannelPrefs) => void manager.session(deviceId)?.setChannelPrefs(next).catch(() => undefined);

  // A client can start with only messages from its history to tell which characters it has.
  const known = useMemo(() => (usable ? charactersOf(account, items) : []), [account, items, usable]);
  useEffect(() => {
    if (usable && !place.character && known[0]) setPlace({ deviceId, character: known[0] });
  }, [usable, place.character, known[0]]);

  useEffect(() => store(LAST_CHAT_KEY, JSON.stringify(lastChat)), [lastChat]);

  const here = viewKey(deviceId, character ?? "");
  const rows = useMemo(() => (character ? chatRows(prefs, character, items) : []), [prefs, character, items]);
  // A notification's chat waits for history, since the first group showing its message goes by list order.
  const notice = awaited.current;
  const noticed = notice && loaded && character ? chatForKey(rows, prefs, notice) : null;
  // The open chat belongs to one character; another one starts on its list, or on its last chat when wide.
  const open = notice ? noticed : opened?.place === here ? opened.key : wide ? lastChat[here] ?? null : null;
  const chat = character ? findChat(rows, character, open, prefs) : null;
  const phoneChat = !wide && !!chat;
  const listRef = useRef<HTMLDivElement>(null);
  const opener = useRef<Element | null>(null);

  function show(key: string | null) {
    setOpened({ place: here, key });
    if (key) setLastChat((views) => remember(views, here, key));
  }

  // Edits must keep the stored group, not the row's copy without tells.
  function menuFor(row: ChatRow): MenuTarget | null {
    if (row.kind === "dm") return row.partner ? { kind: "dm", key: row.key, partner: row.partner } : null;
    const custom = prefs.custom.find((c) => customKey(c) === row.key);
    return custom ? { kind: "group", custom } : null;
  }

  // Counted once history is in, so the NEW divider anchors on the loaded messages.
  const shownKey = loaded ? chat?.key : undefined;
  const includes = useMemo(() => (shownKey ? (key: string) => channelIncludes(prefs, shownKey, key) : null), [shownKey, prefs]);
  const unreadCount = useMemo(() => (includes ? unread.count(deviceId, includes) : 0), [deviceId, includes]);

  // Client settings open on top of app settings, so back steps out to them first.
  useBackClose(() => setSettings(null), !!settings);
  useBackClose(() => setSettings({ app: true }), !!settings && "deviceId" in settings);
  useBackClose(() => setSwitching(false), switching);
  useBackClose(() => setMenu(null), !!menu);
  useBackClose(() => setNewChat(false), newChat);
  useBackClose(() => setEditing(null), !!editing);
  useBackClose(() => show(null), phoneChat);

  const covered = !!settings || switching || !!menu || !!editing || newChat;

  useEffect(() => {
    unread.setOpen(includes && !covered ? { deviceId, includes } : null);
  }, [unread, deviceId, includes, covered]);

  useEffect(() => () => unread.setOpen(null), [unread]);

  // The phone's chat screen also closes on Escape, unless a sheet over it takes it.
  useEffect(() => {
    if (!phoneChat || covered) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") show(null);
    };
    addEventListener("keydown", onKey);
    return () => removeEventListener("keydown", onKey);
  }, [phoneChat, covered, here]);

  // The phone hides the list under the chat, so focus goes back to the tapped row, or the list, when the chat closes.
  useEffect(() => {
    if (!phoneChat) return;
    return () => {
      const row = opener.current;
      opener.current = null;
      (row instanceof HTMLElement && listRef.current?.contains(row) ? row : listRef.current)?.focus();
    };
  }, [phoneChat]);

  // A notification tap lands in the chat, not behind a sheet.
  useEffect(() => {
    setNewChat(false);
    setEditing(null);
    setMenu(null);
    setSettings(null);
    setSwitching(false);
  }, [notified]);

  // Leaving a character forgets its open chat. Declared before the notice effect, so a notification landing on another character still opens.
  useEffect(() => setOpened(null), [here]);

  useEffect(() => {
    if (!notice || !loaded || !character) return;
    awaited.current = null;
    show(noticed);
  }, [notice, loaded, here]);

  // A removed client has no settings left; its place shows Remove and Pair again instead.
  function openClientSettings(id: string) {
    if (accounts.find((a) => a.deviceId === id)?.status === "revoked") {
      setSettings(null);
      setPlace({ deviceId: id, character: null });
      return;
    }
    setSettings({ deviceId: id });
  }

  const me = describeCharacter(account, character);
  const otherTells = unread.total(accounts.map((a) => a.deviceId)).tells - (character ? unread.summary(deviceId, character).tells : 0);

  function listBody() {
    if (account.status === "revoked") return <RevokedNotice inline label={account.label} onRemove={() => manager.remove(deviceId)} onPairAgain={onPairAgain} />;
    if (account.state.status === "pending") return <PendingScreen inline state={account.state} onCancel={() => manager.remove(deviceId)} />;
    if (!character) return <p class="hint list-empty">No characters yet. Log in to a character with the plugin running.</p>;
    return undefined;
  }

  function pane() {
    if (account.status === "revoked") return <RevokedNotice label={account.label} onRemove={() => manager.remove(deviceId)} onPairAgain={onPairAgain} />;
    if (account.state.status === "pending") return <PendingScreen state={account.state} onCancel={() => manager.remove(deviceId)} />;
    if (!chat) return <p class="hint app-empty">Pick a chat to start</p>;
    return (
      <ChatPane
        key={`${deviceId}-${epoch}`}
        account={account}
        prefs={prefs}
        chat={chat}
        items={items}
        unreadCount={unreadCount}
        sends={sends}
        pending={pending}
        wide={wide}
        onBack={() => show(null)}
        onMenu={() => setMenu(menuFor(chat))}
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

  // The list stays mounted under a phone's chat screen, so going back keeps its scroll.
  // A notification remounts it, which also closes its options menu.
  return (
    <>
      <div class={`app${wide ? " wide" : ""}`}>
        <div ref={listRef} class="app-list" tabIndex={-1} hidden={phoneChat}>
          <ChatList
            key={notified}
            me={me}
            otherTells={otherTells}
            rows={rows}
            selected={chat?.key ?? null}
            canStart={!!character}
            body={listBody()}
            unreadOf={(r) => rowUnread(unread, deviceId, prefs, r.key)}
            onOpen={(r) => {
              opener.current = document.activeElement;
              show(r.key);
            }}
            onRowMenu={(r) => setMenu(menuFor(r))}
            onSwitch={() => setSwitching(true)}
            onSettings={() => setSettings({ app: true })}
            onPair={onAdd}
            onNew={() => setNewChat(true)}
          />
        </div>
        {(wide || chat) && <div class="app-chat">{pane()}</div>}
      </div>
      {settingsSheet()}
      {switching && (
        <CharacterSheet
          groups={characterGroups(accounts, (a) => charactersOf(a, a.deviceId === deviceId ? items : []), (id, name) => characterUnread(unread, id, manager.session(id)?.getState().channelPrefs ?? EMPTY_CHANNEL_PREFS, name))}
          current={place}
          onPick={(next) => {
            awaited.current = null;
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
      {newChat && character && (
        <NewChat
          character={character}
          {...newChatPeople(account.state.contacts, partnersOf(items, character, []), character)}
          onOpenDm={(partner) => {
            setNewChat(false);
            show(`t|${character}|${partner}`);
          }}
          onNewGroup={() => setEditing({})}
          onClose={() => setNewChat(false)}
        />
      )}
      {editing && character && (
        <GroupEditor
          character={character}
          custom={editing.custom}
          relayChannels={account.state.relayChannels}
          onSave={(c) => {
            save(saveCustom(prefs, c));
            if (editing.custom) return;
            setNewChat(false);
            show(customKey(c));
          }}
          onClose={() => setEditing(null)}
        />
      )}
    </>
  );
}
