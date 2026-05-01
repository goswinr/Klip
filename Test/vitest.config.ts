import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: true,
    environment: 'node',
    include: ['tests/**/*.{test,spec}.ts'],
    exclude: ['node_modules', '_ts/fable_modules', '.git'],
    benchmark: {
      include: ['bench/**/*.bench.ts']
    }
  }
});
