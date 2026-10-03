import type { AccountView } from "../core/accounts";
import { STATUS_LABELS } from "./format";
import type { Place } from "./place";
import type { UnreadSummary } from "./unread";

export interface CharacterOption {
  place: Place;
  name: string;
  world?: string;
  status: string;
  online: boolean;
  tells: number;
  unread: boolean;
}

export interface CharacterGroup {
  deviceId: string;
  label: string;
  options: CharacterOption[];
}

// Only a client's logged-in character has a known world and a live status.
export function describeCharacter(account: AccountView, character: string | null): { name: string; world?: string; status: string; online: boolean } {
  if (account.status === "revoked") return { name: account.label, status: STATUS_LABELS.revoked, online: false };
  if (account.state.status === "pending") return { name: account.label, status: STATUS_LABELS.pending, online: false };
  if (!character) return { name: account.label, status: "No characters yet", online: false };
  if (character !== account.character) return { name: character, status: "Not logged in", online: false };
  return { name: character, world: account.characterWorld, status: STATUS_LABELS[account.state.status], online: account.state.status === "online" };
}

export function characterGroups(
  accounts: AccountView[],
  charactersOf: (account: AccountView) => string[],
  summary: (deviceId: string, character: string) => UnreadSummary,
): CharacterGroup[] {
  return accounts.map((account) => {
    const usable = account.status === "active" && account.state.status !== "pending";
    const characters = usable ? charactersOf(account) : [];
    const places = characters.length > 0 ? characters : [null];
    return {
      deviceId: account.deviceId,
      label: account.label,
      options: places.map((character) => {
        const unread = character ? summary(account.deviceId, character) : { unread: false, tells: 0 };
        return { place: { deviceId: account.deviceId, character }, ...describeCharacter(account, character), tells: unread.tells, unread: unread.unread };
      }),
    };
  });
}
