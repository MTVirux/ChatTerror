import { useEffect, useState } from "preact/hooks";
import type { ChatChannel } from "../core/protocol";
import type { Session, SessionState } from "../core/session";
import { channelColor, channelLabel, isIos } from "./format";
import { CACHE_SIZES, loadCacheLimit, loadTheme, saveCacheLimit, saveTheme, type ThemeChoice } from "./theme";

function needsHomeScreen(): boolean {
  return isIos() && (navigator as Navigator & { standalone?: boolean }).standalone === false;
}

export function SettingsView({ session, state, onClose, onCacheCleared, onUnpaired }: {
  session: Session;
  state: SessionState;
  onClose: () => void;
  onCacheCleared: () => void;
  onUnpaired: () => void;
}) {
  const [theme, setTheme] = useState<ThemeChoice>(loadTheme);
  const [cacheLimit, setCacheLimit] = useState(loadCacheLimit);
  const [pushBusy, setPushBusy] = useState(false);
  const [pushError, setPushError] = useState("");
  const [confirm, setConfirm] = useState<"clear" | "unpair" | null>(null);
  const [cleared, setCleared] = useState(false);
  const [unpairing, setUnpairing] = useState(false);
  const iosBlocked = needsHomeScreen();

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    addEventListener("keydown", onKey);
    return () => removeEventListener("keydown", onKey);
  }, [onClose]);

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
    session.setMuted(muted);
  }

  function chooseTheme(choice: ThemeChoice) {
    setTheme(choice);
    saveTheme(choice);
  }

  function chooseCacheLimit(n: number) {
    setCacheLimit(n);
    saveCacheLimit(n);
    session.setCacheLimit(n);
  }

  async function clearCache() {
    setConfirm(null);
    await session.clearCache();
    setCleared(true);
    onCacheCleared();
  }

  async function unpair() {
    setUnpairing(true);
    try {
      await session.unpair();
    } finally {
      onUnpaired();
    }
  }

  return (
    <div class="sheet-backdrop" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <section class="sheet" role="dialog" aria-modal="true" aria-labelledby="settings-title">
        <header class="sheet-head">
          <h2 id="settings-title">Settings</h2>
          <button class="btn ghost" onClick={onClose}>Done</button>
        </header>

        <div class="sheet-body">
          <section class="group">
            <h3>Notifications</h3>
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
              <h3>Mute notifications for</h3>
              <div class="mute-grid">
                {state.relayChannels.map((c) => (
                  <label class="mute" style={{ "--c": channelColor(c) }}>
                    <input type="checkbox" checked={state.mutedChannels.includes(c)} onChange={() => toggleMute(c)} />
                    <span>{channelLabel(c)}</span>
                  </label>
                ))}
              </div>
              <p class="note">Muted channels still show up here, they just don't buzz your phone.</p>
            </section>
          )}

          <section class="group">
            <h3>Appearance</h3>
            <div class="segmented" role="radiogroup" aria-label="Theme">
              {(["system", "light", "dark"] as ThemeChoice[]).map((t) => (
                <button role="radio" aria-checked={theme === t} class={theme === t ? "on" : ""} onClick={() => chooseTheme(t)}>
                  {t === "system" ? "Automatic" : t === "light" ? "Light" : "Dark"}
                </button>
              ))}
            </div>
          </section>

          <section class="group">
            <h3>Stored messages</h3>
            <label class="row">
              <span>Keep on this device</span>
              <select value={String(cacheLimit)} onChange={(e) => chooseCacheLimit(Number(e.currentTarget.value))}>
                {CACHE_SIZES.map((n) => <option value={String(n)}>{n.toLocaleString()} messages</option>)}
              </select>
            </label>
            {confirm === "clear" ? (
              <div class="confirm">
                <span>Delete all stored messages from this device?</span>
                <button class="btn danger" onClick={clearCache}>Delete</button>
                <button class="btn ghost" onClick={() => setConfirm(null)}>Keep</button>
              </div>
            ) : (
              <button class="btn secondary" onClick={() => { setCleared(false); setConfirm("clear"); }}>
                {cleared ? "Messages deleted" : "Delete stored messages"}
              </button>
            )}
          </section>

          <section class="group">
            <h3>This device</h3>
            {state.character && <p class="kv"><span>Character</span><span>{state.character}</span></p>}
            {state.fingerprint && <p class="kv"><span>Security number</span><span class="mono">{state.fingerprint}</span></p>}
            {confirm === "unpair" ? (
              <div class="confirm">
                <span>Unpair this device? Stored messages are deleted and you'll need a new code to pair again.</span>
                <button class="btn danger" onClick={unpair} disabled={unpairing}>{unpairing ? "Unpairing..." : "Unpair"}</button>
                <button class="btn ghost" onClick={() => setConfirm(null)}>Cancel</button>
              </div>
            ) : (
              <button class="btn danger-outline" onClick={() => setConfirm("unpair")}>Unpair this device</button>
            )}
          </section>
        </div>
      </section>
    </div>
  );
}
