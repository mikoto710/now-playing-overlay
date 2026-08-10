import { resolve } from "node:path";
import { defineConfig } from "vite";
import { viteSingleFile } from "vite-plugin-singlefile";

export default defineConfig({
  plugins: [viteSingleFile({ removeViteModuleLoader: true })],
  base: "/",
  build: {
    // OBS 31.0.1 ships CEF/Chromium 127; older OBS releases are not an M4B compatibility promise.
    target: "chrome127",
    outDir: "dist",
    emptyOutDir: true,
    rollupOptions: {
      input: resolve(import.meta.dirname, "NowPlaying.html"),
    },
  },
  test: {
    environment: "node",
    include: ["tests/**/*.test.ts"],
  },
});
