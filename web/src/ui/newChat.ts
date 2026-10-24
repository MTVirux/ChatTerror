import type { TellContact } from "../core/protocol";

export interface Person {
  partner: string;
  name: string;
  world?: string;
}

export function newChatPeople(contacts: TellContact[], partners: string[], character: string): { friends: Person[]; recent: Person[] } {
  const friends = contacts
    .filter((c) => c.character === character)
    .map((c) => ({ partner: `${c.name}@${c.world}`, name: c.name, world: c.world }))
    .sort((a, b) => a.name.localeCompare(b.name));
  const known = new Set(friends.map((f) => f.partner.toLowerCase()));
  const recent = partners
    .filter((p) => !known.has(p.toLowerCase()))
    .map((partner) => {
      const [name, world] = partner.split("@");
      return { partner, name, world };
    });
  return { friends, recent };
}
