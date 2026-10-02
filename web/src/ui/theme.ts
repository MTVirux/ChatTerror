export type ThemeChoice = "system" | "light" | "dark";

const THEME_KEY = "chatterror.theme";

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage can be unavailable in private mode; the choice then lasts for this visit only.
  }
}

export function loadTheme(): ThemeChoice {
  const value = read(THEME_KEY);
  return value === "light" || value === "dark" ? value : "system";
}

const STATUS_BAR = { light: "#ffffff", dark: "#313338" };

export function applyTheme(choice: ThemeChoice) {
  const root = document.documentElement;
  if (choice === "system") delete root.dataset.theme;
  else root.dataset.theme = choice;
  // index.html has one theme-color per color scheme; a manual choice makes both match it.
  for (const meta of document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')) {
    const own = meta.media.includes("dark") ? "dark" : "light";
    meta.content = STATUS_BAR[choice === "system" ? own : choice];
  }
}

export function saveTheme(choice: ThemeChoice) {
  write(THEME_KEY, choice);
  applyTheme(choice);
}
