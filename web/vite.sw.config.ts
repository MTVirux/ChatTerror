import { defineConfig } from "vite";

// Service workers registered without type "module" need a single classic script.
export default defineConfig({
  publicDir: false,
  build: {
    outDir: "../src/ChatTerror.Server/wwwroot",
    emptyOutDir: false,
    lib: {
      entry: "src/sw.ts",
      formats: ["iife"],
      name: "sw",
      fileName: () => "sw.js",
    },
  },
});
