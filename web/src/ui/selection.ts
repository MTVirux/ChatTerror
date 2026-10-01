import type { AccountView } from "../core/accounts";

export type Selection = "all" | "add" | string;

export function validSelection(selection: Selection, accounts: AccountView[]): Selection {
  if (accounts.length === 0) return "add";
  if (selection === "add") return "add";
  if (selection === "all") return accounts.length > 1 ? "all" : accounts[0].deviceId;
  return accounts.some((a) => a.deviceId === selection) ? selection : accounts[0].deviceId;
}

export function initialSelection(accounts: AccountView[], hash: string, stored: string | null): Selection {
  if (accounts.length > 0 && hash.startsWith("#pair=")) return "add";
  const fromLink = hash.match(/^#account=(.+)$/);
  if (fromLink) return validSelection(decodeURIComponent(fromLink[1]), accounts);
  return validSelection(stored ?? accounts[0]?.deviceId ?? "add", accounts);
}
