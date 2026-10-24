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

// Uses the listed spelling, so a typed name opens the DM key that already holds the messages.
export function matchPerson(people: Person[], typed: string): string {
  return people.find((p) => p.partner.toLowerCase() === typed.toLowerCase())?.partner ?? gameSpelling(typed);
}

// How the game capitalises a "Name Surname@World", so a new DM opens under the key its tells will come back with.
export function gameSpelling(typed: string): string {
  const [name, world] = typed.split("@");
  const capital = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);
  const spelled = name.split(" ").map(capital).join(" ");
  return world ? `${spelled}@${capital(world.toLowerCase())}` : spelled;
}
