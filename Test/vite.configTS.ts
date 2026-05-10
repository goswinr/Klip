import { defineConfig } from "vite";

export default defineConfig({
  build: {
    minify: false,
    lib: {
      entry: "_ts/Src/Klip",
      name: "Klip",
    },
    sourcemap: true,
    rollupOptions: {
      output: [
        {
          dir: "_distTS",
          format: "es"
        }
      ]
    }
  }
});