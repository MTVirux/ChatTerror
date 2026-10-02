export function readPref(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function writePref(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage can be unavailable in private mode; the choice then lasts for this visit only.
  }
}

const SHOW_EMPTY_KEY = "chatterror.showEmptyChannels";

export function loadShowEmpty(): boolean {
  return readPref(SHOW_EMPTY_KEY) === "1";
}

export function saveShowEmpty(show: boolean) {
  writePref(SHOW_EMPTY_KEY, show ? "1" : "0");
}
