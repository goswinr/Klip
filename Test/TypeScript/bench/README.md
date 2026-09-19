# Benchmarks


Klip doesn't have its own `Point64` object but uses a flat interleaved `xys[]`
array in `Path64`.

The internal engine objects have direct `x`, `y`, and optional `z`
properties instead of nested point objects, and the scanline engine operates on
these directly. In total, fewer JavaScript objects are allocated.


This folder `bench/` ports the applicable benchmarks from
[clipper2-ts/bench/](https://github.com/countertype/clipper2-ts/tree/main/bench) (skipping internal BigInt fallback,
triangulation, and offset/inflate cases, which Klip doesn't expose). Each
`describe` group times the same operation against:

- **clipper2-ts** - imported from the published
  [`clipper2-ts`](https://www.npmjs.com/package/clipper2-ts) npm package, not
  the local source
- **clipper2-wasm** - imported from the published
  [`clipper2-wasm`](https://www.npmjs.com/package/clipper2-wasm) npm package, not
  the local source
- **Klip** - imported from `../_dist/Klip.mjs` (the production Vite bundle; types mirror `../_tsc/Src/Klip.d.ts`)

| File                                 | Notes                                                         |
| ------------------------------------ | ------------------------------------------------------------- |
| `bench/bench-stats.ts`               | Copied verbatim from clipper2-ts                              |
| `bench/test-data.ts`                 | Copy with import switched to `clipper2-ts` npm                |
| `bench/klip-helpers.ts`              | Duck-typed `{xys, zs}` adapter and Klip ops re-export         |
| `bench/wasm-helpers.ts`              | Published `clipper2-wasm` loader and `Paths64` adapter        |
| `bench/clipping-operations.bench.ts` | Side-by-side clipper2-ts vs Klip vs clipper2-wasm benches     |
| `bench/scanline-threshold.mjs`       | Sweep of the scanline-container threshold on two grid shapes  |

Klip inputs are pre-converted to its flat-buffer `Path64` shape outside the
timed regions (mirroring how clipper2-ts excludes input setup). The adapter
duck-types `Path64` and `PolyTree64` instead of importing the classes, so
`_dist/Klip.mjs` doesn't need to expose internals.

Run:

```bash
npm run build   # rebuild _dist/Klip.mjs if F# sources changed
npm run bench   # vitest bench --run
npm run bench:scanline   # scanline-container threshold sweep
```

The Vitest benchmark reporter keeps Klip as the final summary reference: Klip
is printed as `1.00x reference`, and the other implementations are shown as
throughput ratios versus Klip instead of versus the fastest run.

### Results

Latest local run (2026-09-19; Node 24.7.0) of `npm run bench`, 30 side-by-side Vitest
benchmark groups. Averages below are the geometric mean of per-benchmark
throughput (`hz`) ratios, so each benchmark group contributes equally.

| Comparison | Average relative performance | Wins |
| ---------- | ---------------------------- | ---- |
| Klip vs `clipper2-ts` | `1.32x` as fast (`+32%`) | Klip faster in 29 / 30 groups |
| Klip vs `clipper2-wasm` | `0.73x` as fast (`-27%`) | Klip faster in 1 / 30 groups |
| `clipper2-wasm` vs `clipper2-ts` | `1.8x` as fast (`+81%`) | `clipper2-wasm` faster in 30 / 30 groups |

`clipper2-wasm` was the fastest implementation in 29 / 30 groups. Klip's only
numerical win was the two-overlapping-rectangles union, which was effectively a tie.
Benchmark samples are intentionally short; the geometric means avoid letting the
fastest tiny cases dominate the summary.

### Scanline threshold

`npm run bench:scanline` measures the median time per union while varying the
number of pending scanlines at which the engine switches from its small array to
the heap-plus-set container. The default is 64. At the largest measured workload
(8,192 local minima), it was within 1% of the best threshold on both shapes and
substantially better than always using the array.

| Workload at 8,192 minima | Heap only | Default `T=64` | Best threshold | Array only |
| ------------------------ | --------: | -------------: | -------------: | ---------: |
| Rotated squares (all Ys distinct) | 47.132 ms | 46.901 ms | 46.613 ms (`T=256`) | 63.940 ms |
| Upright diamonds (shared row Ys) | 12.576 ms | 12.407 ms | 12.382 ms (`T=16`) | 13.133 ms |
