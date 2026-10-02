import { useState } from "preact/hooks";
import type { AccountManager, AccountView } from "../core/accounts";
import { offerAndroidApp, STATUS_LABELS } from "./format";
import { ChevronRightIcon } from "./icons";
import { initials, senderColor } from "./identity";
import { SettingsGroup, SettingsSheet } from "./SettingsSheet";
import { loadTheme, saveTheme, type ThemeChoice } from "./theme";

const CACHE_SIZES = [500, 2000, 5000];
const THEMES: { value: ThemeChoice; label: string }[] = [
  { value: "system", label: "Automatic" },
  { value: "light", label: "Light" },
  { value: "dark", label: "Dark" },
];

export function AppSettings({ manager, accounts, onClose, onOpenAccount, onAddAccount }: {
  manager: AccountManager;
  accounts: AccountView[];
  onClose: () => void;
  onOpenAccount: (deviceId: string) => void;
  onAddAccount: () => void;
}) {
  const [theme, setTheme] = useState<ThemeChoice>(loadTheme);
  const [cacheError, setCacheError] = useState("");

  function chooseTheme(choice: ThemeChoice) {
    setTheme(choice);
    saveTheme(choice);
  }

  function chooseCacheLimit(n: number) {
    setCacheError("");
    manager.setCacheLimit(n).catch(() => setCacheError("Couldn't change how many messages are kept. Try again."));
  }

  return (
    <SettingsSheet title="Settings" onClose={onClose}>
      <SettingsGroup label="Accounts">
        {accounts.map((a) => (
          <button class="settings-row" key={a.deviceId} onClick={() => onOpenAccount(a.deviceId)}>
            <span class={`dm-avatar${a.status === "revoked" ? " revoked" : ""}`} style={{ "--c": senderColor(a.deviceId) }} aria-hidden="true">{initials(a.label)}</span>
            <span class="settings-text">
              {a.label}
              <small>{a.status === "revoked" ? "Removed" : STATUS_LABELS[a.state.status]}</small>
            </span>
            <ChevronRightIcon />
          </button>
        ))}
        <button class="settings-row accent" onClick={onAddAccount}>
          <span class="settings-text">Add account</span>
        </button>
      </SettingsGroup>

      {offerAndroidApp() && (
        <SettingsGroup label="Android app" note={<p class="settings-note">Keeps your accounts linked more reliably than the browser.</p>}>
          <a class="settings-row accent" href="/ChatTerror.apk" download>
            <span class="settings-text">Download app</span>
          </a>
        </SettingsGroup>
      )}

      <SettingsGroup label="Appearance">
        <div role="radiogroup" aria-label="Theme">
          {THEMES.map((t) => (
            <button class="settings-row" role="radio" aria-checked={theme === t.value} onClick={() => chooseTheme(t.value)}>
              <span class="settings-text">{t.label}</span>
              <span class="radio" aria-hidden="true" />
            </button>
          ))}
        </div>
      </SettingsGroup>

      <SettingsGroup label="Stored messages" note={cacheError && <p class="settings-note error">{cacheError}</p>}>
        <label class="settings-row">
          <span class="settings-text">Keep per account</span>
          <select value={String(manager.cacheLimit())} onChange={(e) => chooseCacheLimit(Number(e.currentTarget.value))}>
            {CACHE_SIZES.map((n) => <option value={String(n)}>{n.toLocaleString()} messages</option>)}
          </select>
        </label>
      </SettingsGroup>
    </SettingsSheet>
  );
}
