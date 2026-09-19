import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// The .NET sample serves the console under /console; the dev server proxies the real API so a running
// samples/ProtoTest.SampleApp instance is the only backend. Point VITE_API_TARGET elsewhere to move it.
const target = process.env.VITE_API_TARGET ?? "http://localhost:60546";

export default defineConfig({
  base: "/console/",
  plugins: [vue()],
  server: {
    port: 5180,
    strictPort: true,
    proxy: {
      "/api": { target },
      "/graphql": { target, ws: true },
      "/test-support": { target },
      "/health": { target }
    }
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: false
  }
});
