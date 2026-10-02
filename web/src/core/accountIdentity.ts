export const PALETTE = ["#5865f2", "#3ba55c", "#faa61a", "#ed4245", "#eb459e", "#9b59b6", "#1abc9c", "#e67e22"];

export function hashColor(text: string): string {
  let hash = 0;
  for (const ch of text) hash = (hash * 31 + ch.charCodeAt(0)) | 0;
  return PALETTE[Math.abs(hash) % PALETTE.length];
}

export interface LabelSource {
  name?: string;
  fallback: string;
}

// Invisible characters, case and spacing can't make two labels look different.
function labelKey(label: string): string {
  return label.normalize("NFKC").replace(/\p{Cf}/gu, "").replace(/\s+/g, " ").trim().toLowerCase();
}

// Plugins choose their own character names, so one could copy another account's.
// Custom names claim their label first, then the rest in pairing order get " (2)", " (3)"...
export function uniqueLabels(sources: LabelSource[]): string[] {
  const labels: string[] = [];
  const taken = new Set<string>();
  const named = sources.map((_, i) => i).sort((a, b) => Number(!sources[a].name) - Number(!sources[b].name));
  for (const i of named) {
    const base = sources[i].name || sources[i].fallback;
    let label = base;
    for (let n = 2; taken.has(labelKey(label)); n++) label = `${base} (${n})`;
    taken.add(labelKey(label));
    labels[i] = label;
  }
  return labels;
}

// Keeps each account's hashed color unless an earlier account already has it.
export function accountColors(deviceIds: string[]): string[] {
  const used = new Set<string>();
  return deviceIds.map((id) => {
    const hashed = hashColor(id);
    const color = !used.has(hashed) ? hashed : PALETTE.find((c) => !used.has(c)) ?? hashed;
    used.add(color);
    return color;
  });
}
