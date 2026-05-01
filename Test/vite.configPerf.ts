import { defineConfig } from "vite";

export default defineConfig({
  build: {
    minify: false , //"terser", terser doesn't make it faster
    lib: {
      entry: "_jsPerf/Src/Klip.js",
      name: "Klip",
    },
    sourcemap: true,
    rollupOptions: {
      output: [
        {
          dir: "_distPerf",
          format: "es"
        }
      ]
    }
  }
});