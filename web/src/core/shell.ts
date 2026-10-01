export function referencedAssets(html: string): string[] {
  return [...html.matchAll(/(?:src|href)="(\/assets\/[^"]+)"/g)].map((m) => m[1]);
}

export function staleAssets(cachedPaths: string[], html: string): string[] {
  const current = new Set(referencedAssets(html));
  return cachedPaths.filter((path) => path.startsWith("/assets/") && !current.has(path));
}
