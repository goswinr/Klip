import { defineConfig } from "vite";

export default defineConfig({
  build: {
    minify: false,
    lib: {
      entry: "_js/Src/Klip.js",
      name: "Klip",
    },
    sourcemap: true,
    rollupOptions: {
      output: [
        {
          dir: "_dist",
          format: "es"
        }
      ]
    }
  }
});