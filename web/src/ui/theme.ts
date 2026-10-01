export type ThemeChoice = "system" | "light" | "dark";

const THEME_KEY = "chatterror.theme";
const CACHE_KEY = "chatterror.cacheLimit";

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

export function applyTheme(choice: ThemeChoice) {
  const root = document.documentElement;
  if (choice === "system") delete root.dataset.theme;
  else root.dataset.theme = choice;
}

export function saveTheme(choice: ThemeChoice) {
  write(THEME_KEY, choice);
  applyTheme(choice);
}

export const CACHE_SIZES = [500, 2000, 5000];

export function loadCacheLimit(): number {
  const value = Number(read(CACHE_KEY));
  return CACHE_SIZES.includes(value) ? value : 2000;
}

export function saveCacheLimit(n: number) {
  write(CACHE_KEY, String(n));
}
