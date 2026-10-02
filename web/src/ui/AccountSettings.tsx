import { useEffect, useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import type { ChatChannel } from "../core/protocol";
import { findAccount } from "../core/registry";
import type { Session } from "../core/session";
import { channelColor, channelLabel, isIos } from "./format";
import { SettingsGroup, SettingsSheet } from "./SettingsSheet";

function needsHomeScreen(): boolean {
  return isIos() && (navigator as Navigator & { standalone?: boolean }).standalone === false;
}

export function AccountSettings({ manager, account, session, onClose, onCacheCleared }: {
  manager: AccountManager;
  account: AccountView;
  session: Session;
  onClose: () => void;
  onCacheCleared: () => void;
}) {
  const state = account.state;
  const [savedName, setSavedName] = useState("");
  const [name, setName] = useState("");
  const [renameError, setRenameError] = useState("");
  const [pushBusy, setPushBusy] = useState(false);
  const [pushError, setPushError] = useState("");
  const [confirm, setConfirm] = useState<"clear" | "unpair" | null>(null);
  const [cleared, setCleared] = useState(false);
  const [unpairing, setUnpairing] = useState(false);
  const [unpairError, setUnpairError] = useState("");
  const [muteError, setMuteError] = useState("");
  const [cacheError, setCacheError] = useState("");
  const iosBlocked = needsHomeScreen();

  // The view only has the display label, which falls back to the character, so the custom name comes from the registry.
  useEffect(() => {
    findAccount(account.deviceId)
      .then((record) => {
        const current = record?.name ?? "";
        setSavedName(current);
        setName((typed) => typed || current);
      })
      .catch(() => {});
  }, [account.deviceId]);

  async function rename(event: Event) {
    event.preventDefault();
    setRenameError("");
    try {
      await manager.rename(account.deviceId, name);
      setName(name.trim());
      setSavedName(name.trim());
    } catch {
      setRenameError("Couldn't rename this account. Try again.");
    }
  }

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
    <SettingsSheet title={account.label} onClose={onClose}>
      <SettingsGroup
        label="Name"
        note={renameError ? <p class="settings-note error">{renameError}</p> : <p class="settings-note">Leave it empty to use the character name.</p>}
      >
        <form class="settings-row name-row" onSubmit={rename}>
          <input
            value={name}
            onInput={(e) => setName(e.currentTarget.value)}
            placeholder={account.character ?? "Account name"}
            aria-label="Account name"
            maxLength={40}
          />
          <button type="submit" class="btn primary small" disabled={name.trim() === savedName}>Save</button>
        </form>
      </SettingsGroup>

      <SettingsGroup
        label="Notifications"
        note={
          <>
            {iosBlocked && <p class="settings-note">On iPhone and iPad, add this page to your Home Screen first: tap Share, then Add to Home Screen, and open it from there.</p>}
            {pushError && <p class="settings-note error">{pushError}</p>}
          </>
        }
      >
        <label class="settings-row">
          <span class="settings-text">
            Notify me about new messages
            <small>Arrives even when this app is closed.</small>
          </span>
          <input type="checkbox" class="switch" checked={state.pushEnabled} disabled={pushBusy || iosBlocked} onChange={togglePush} />
        </label>
      </SettingsGroup>

      {state.relayChannels.length > 0 && (
        <SettingsGroup
          label="Mute notifications for"
          note={
            <>
              <p class="settings-note">Muted channels still show up here, they just don't buzz your phone.</p>
              {muteError && <p class="settings-note error">{muteError}</p>}
            </>
          }
        >
          {state.relayChannels.map((c) => (
            <label class="settings-row" key={c}>
              <span class="hash" style={{ "--c": channelColor(c) }} aria-hidden="true">#</span>
              <span class="settings-text">{channelLabel(c)}</span>
              <input type="checkbox" class="switch" checked={state.mutedChannels.includes(c)} onChange={() => toggleMute(c)} />
            </label>
          ))}
        </SettingsGroup>
      )}

      <SettingsGroup label="Stored messages" note={cacheError && <p class="settings-note error">{cacheError}</p>}>
        {confirm === "clear" ? (
          <div class="confirm">
            <span>Delete this account's stored messages from this phone?</span>
            <button class="btn danger" onClick={clearCache}>Delete</button>
            <button class="btn ghost" onClick={() => setConfirm(null)}>Keep</button>
          </div>
        ) : (
          <button class="settings-row danger" onClick={() => { setCleared(false); setConfirm("clear"); }}>
            <span class="settings-text">{cleared ? "Messages deleted" : "Delete stored messages"}</span>
          </button>
        )}
      </SettingsGroup>

      <SettingsGroup label="Pairing" note={unpairError && <p class="settings-note error">{unpairError}</p>}>
        {state.character && (
          <div class="settings-row">
            <span class="settings-text">Character</span>
            <span class="settings-value">{state.character}</span>
          </div>
        )}
        {state.fingerprint && (
          <div class="settings-row">
            <span class="settings-text">Security number</span>
            <span class="settings-value mono">{state.fingerprint}</span>
          </div>
        )}
        {confirm === "unpair" ? (
          <div class="confirm">
            <span>Unpair this account? Its stored messages are deleted and you'll need a new code to pair it again.</span>
            <button class="btn danger" onClick={unpair} disabled={unpairing}>{unpairing ? "Unpairing..." : "Unpair"}</button>
            <button class="btn ghost" onClick={() => setConfirm(null)}>Cancel</button>
          </div>
        ) : (
          <button class="settings-row danger" onClick={() => setConfirm("unpair")}>
            <span class="settings-text">Unpair this account</span>
          </button>
        )}
      </SettingsGroup>
    </SettingsSheet>
  );
}
