import { hashColor } from "../core/accountIdentity";

export function initials(name: string): string {
  const words = name.split("@")[0].split(/\s+/).map((w) => w.replace(/[^A-Za-z]/g, "")).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

export function senderColor(name: string): string {
  return hashColor(name);
}

// Served by the relay that served the page, which looks the character up on the Lodestone.
export function portraitUrl(name: string | undefined, world: string | undefined): string | undefined {
  if (!name || !world) return undefined;
  return `/api/portrait?name=${encodeURIComponent(name)}&world=${encodeURIComponent(world)}`;
}
