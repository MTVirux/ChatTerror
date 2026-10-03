import { useState } from "preact/hooks";
import { portraitUrl } from "./identity";

const failed = new Set<string>();

// Covers the initials it sits in, which show through when there's no portrait.
export function Portrait({ name, world }: { name?: string; world?: string }) {
  const [, setFailures] = useState(0);
  const url = portraitUrl(name, world);
  if (!url || failed.has(url)) return null;
  function onError() {
    failed.add(url!);
    setFailures((n) => n + 1);
  }
  return <img class="portrait" src={url} alt="" loading="lazy" onError={onError} />;
}
