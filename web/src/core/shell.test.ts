import { describe, expect, it } from "vitest";
import { referencedAssets, staleAssets } from "./shell";

const html = `<script type="module" crossorigin src="/assets/index-abc.js"></script>
<link rel="stylesheet" crossorigin href="/assets/index-def.css">
<link rel="manifest" href="/manifest.webmanifest" />`;

describe("shell assets", () => {
  it("finds the assets index.html references", () => {
    expect(referencedAssets(html)).toEqual(["/assets/index-abc.js", "/assets/index-def.css"]);
  });

  it("lists cached assets the current index.html no longer references", () => {
    const cached = ["/index.html", "/assets/index-abc.js", "/assets/index-old.js", "/assets/index-def.css", "/icon-192.png"];
    expect(staleAssets(cached, html)).toEqual(["/assets/index-old.js"]);
  });
});
