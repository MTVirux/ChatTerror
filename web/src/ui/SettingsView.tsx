import { useEffect, useRef, useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import type { ChatChannel } from "../core/protocol";
import type { Session } from "../core/session";
import { channelColor, channelLabel, isIos, STATUS_LABELS } from "./format";
import { loadTheme, saveTheme, type ThemeChoice } from "./theme";

const CACHE_SIZES = [500, 2000, 5000];

function needsHomeScreen(): boolean {
  return isIos() && (navigator as Navigator & { standalone?: boolean }).standalone === false;
}

export function SettingsView({ manager, accounts, accountId, onClose, onCacheCleared, onOpenAccount, onAddAccount }: {
  manager: AccountManager;
  accounts: AccountView[];
  accountId: string | null;
  onClose: () => void;
  onCacheCleared: () => void;
  onOpenAccount: (deviceId: string) => void;
  onAddAccount: () => void;
}) {
  const [theme, setTheme] = useState<ThemeChoice>(loadTheme);
  const [cacheError, setCacheError] = useState("");
  const dialogRef = useRef<HTMLElement>(null);
  const account = accountId ? accounts.find((a) => a.deviceId === accountId) : undefined;
  const session = account && manager.session(account.deviceId);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialogRef.current?.focus();
    return () => previous?.focus();
  }, []);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    addEventListener("keydown", onKey);
    return () => removeEventListener("keydown", onKey);
  }, [onClose]);

  function chooseTheme(choice: ThemeChoice) {
    setTheme(choice);
    saveTheme(choice);
  }

  function chooseCacheLimit(n: number) {
    setCacheError("");
    manager.setCacheLimit(n).catch(() => setCacheError("Couldn't change how many messages are kept. Try again."));
  }

  function openAccount(deviceId: string) {
    onOpenAccount(deviceId);
    onClose();
  }

  return (
    <div class="sheet-backdrop" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <section class="sheet" ref={dialogRef} tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby="settings-title">
        <header class="sheet-head">
          <h2 id="settings-title">Settings</h2>
          <button class="btn ghost" onClick={onClose}>Done</button>
        </header>

        <div class="sheet-body">
          {account && session && (
            <AccountSettings manager={manager} account={account} session={session} onClose={onClose} onCacheCleared={onCacheCleared} />
          )}

          {accountId === null && (
            <>
              <h3 class="section-head">Accounts</h3>
              <section class="group">
                {accounts.map((a) => (
                  <button class="account-link" key={a.deviceId} onClick={() => openAccount(a.deviceId)}>
                    <span>{a.label}</span>
                    <small>{a.status === "revoked" ? "Removed" : STATUS_LABELS[a.state.status]}</small>
                  </button>
                ))}
              </section>
            </>
          )}

          <h3 class="section-head">This phone</h3>

          <section class="group">
            <h4>Appearance</h4>
            <div class="segmented" role="radiogroup" aria-label="Theme">
              {(["system", "light", "dark"] as ThemeChoice[]).map((t) => (
                <button role="radio" aria-checked={theme === t} class={theme === t ? "on" : ""} onClick={() => chooseTheme(t)}>
                  {t === "system" ? "Automatic" : t === "light" ? "Light" : "Dark"}
                </button>
              ))}
            </div>
          </section>

          <section class="group">
            <h4>Stored messages</h4>
            <label class="row">
              <span>Keep per account</span>
              <select value={String(manager.cacheLimit())} onChange={(e) => chooseCacheLimit(Number(e.currentTarget.value))}>
                {CACHE_SIZES.map((n) => <option value={String(n)}>{n.toLocaleString()} messages</option>)}
              </select>
            </label>
            {cacheError && <p class="error">{cacheError}</p>}
          </section>

          <section class="group">
            <button class="btn secondary" onClick={onAddAccount}>Add account</button>
          </section>
        </div>
      </section>
    </div>
  );
}

function AccountSettings({ manager, account, session, onClose, onCacheCleared }: {
  manager: AccountManager;
  account: AccountView;
  session: Session;
  onClose: () => void;
  onCacheCleared: () => void;
}) {
  const state = account.state;
  const [pushBusy, setPushBusy] = useState(false);
  const [pushError, setPushError] = useState("");
  const [confirm, setConfirm] = useState<"clear" | "unpair" | null>(null);
  const [cleared, setCleared] = useState(false);
  const [unpairing, setUnpairing] = useState(false);
  const [unpairError, setUnpairError] = useState("");
  const [muteError, setMuteError] = useState("");
  const [cacheError, setCacheError] = useState("");
  const iosBlocked = needsHomeScreen();

  async function togglePush() {
    setPushBusy(true);
    setPushError("");
    try {
      if (state.pushEnabled) {
        await session.disablePush();
      } else if (!(await session.enablePush())) {
        setPushError(
          typeof Notification !== "undefined" && Notification.permission === "denied"
            ? "Notifications are blocked. Allow them for this site in your browser settings."
            : "Couldn't turn on notifications in this browser."
        );
      }
    } catch {
      setPushError("Couldn't change notifications. Try again.");
    } finally {
      setPushBusy(false);
    }
  }

  function toggleMute(channel: ChatChannel) {
    const muted = state.mutedChannels.includes(channel)
      ? state.mutedChannels.filter((c) => c !== channel)
      : [...state.mutedChannels, channel];
    setMuteError("");
    session.setMuted(muted).catch(() => setMuteError("Couldn't save mute settings. Try again."));
  }

  async function clearCache() {
    setConfirm(null);
    setCacheError("");
    try {
      await session.clearCache();
      setCleared(true);
      onCacheCleared();
    } catch {
      setCacheError("Couldn't delete stored messages. Try again.");
    }
  }

  async function unpair() {
    setUnpairing(true);
    setUnpairError("");
    try {
      await manager.remove(account.deviceId);
      onClose();
    } catch {
      setUnpairing(false);
      setUnpairError("Couldn't unpair this account. Try again.");
    }
  }

  return (
    <>
      <h3 class="section-head">{account.label}</h3>

      <section class="group">
        <h4>Notifications</h4>
        <label class="row">
          <span>
            Notify me about new messages
            <small>Arrives even when this app is closed.</small>
          </span>
          <input type="checkbox" class="switch" checked={state.pushEnabled} disabled={pushBusy || iosBlocked} onChange={togglePush} />
        </label>
        {iosBlocked && <p class="note">On iPhone and iPad, add this page to your Home Screen first: tap Share, then Add to Home Screen, and open it from there.</p>}
        {pushError && <p class="error">{pushError}</p>}
      </section>

      {state.relayChannels.length > 0 && (
        <section class="group">
          <h4>Mute notifications for</h4>
          <div class="mute-grid">
            {state.relayChannels.map((c) => (
              <label class="mute" style={{ "--c": channelColor(c) }}>
                <input type="checkbox" checked={state.mutedChannels.includes(c)} onChange={() => toggleMute(c)} />
                <span>{channelLabel(c)}</span>
              </label>
            ))}
          </div>
          <p class="note">Muted channels still show up here, they just don't buzz your phone.</p>
          {muteError && <p class="error">{muteError}</p>}
        </section>
      )}

      <section class="group">
        <h4>Stored messages</h4>
        {confirm === "clear" ? (
          <div class="confirm">
            <span>Delete this account's stored messages from this phone?</span>
            <button class="btn danger" onClick={clearCache}>Delete</button>
            <button class="btn ghost" onClick={() => setConfirm(null)}>Keep</button>
          </div>
        ) : (
          <button class="btn secondary" onClick={() => { setCleared(false); setConfirm("clear"); }}>
            {cleared ? "Messages deleted" : "Delete stored messages"}
          </button>
        )}
        {cacheError && <p class="error">{cacheError}</p>}
      </section>

      <section class="group">
        <h4>Pairing</h4>
        {state.character && <p class="kv"><span>Character</span><span>{state.character}</span></p>}
        {state.fingerprint && <p class="kv"><span>Security number</span><span class="mono">{state.fingerprint}</span></p>}
        {confirm === "unpair" ? (
          <div class="confirm">
            <span>Unpair this account? Its stored messages are deleted and you'll need a new code to pair it again.</span>
            <button class="btn danger" onClick={unpair} disabled={unpairing}>{unpairing ? "Unpairing..." : "Unpair"}</button>
            <button class="btn ghost" onClick={() => setConfirm(null)}>Cancel</button>
          </div>
        ) : (
          <button class="btn danger-outline" onClick={() => setConfirm("unpair")}>Unpair this account</button>
        )}
        {unpairError && <p class="error">{unpairError}</p>}
      </section>
    </>
  );
}
