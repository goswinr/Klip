# Benchmarks


Klip doesn't have its own `Point64` object but uses a flat interleaved `xys[]`
array in `Path64`.

The internal engine objects have direct `x`, `y`, and optional `z`
properties instead of nested point objects, and the scanline engine operates on
these directly. In total, fewer JavaScript objects are allocated.


This folder `bench/` ports the applicable benchmarks from
[clipper2-ts/bench/](https://github.com/countertype/clipper2-ts/tree/main/bench) (skipping internal BigInt fallback
and triangulation cases, which Klip doesn't expose; offset/inflate is exposed
but not yet benchmarked here). Each `describe` group times the same operation
against:

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

Klip inputs are pre-converted to its flat-buffer `Path64` shape outside the
timed regions (mirroring how clipper2-ts excludes input setup). The adapter
duck-types `Path64` and `PolyTree64` instead of importing the classes, so
`_dist/Klip.mjs` doesn't need to expose internals.

Run:

```bash
npm run build   # rebuild _dist/Klip.mjs if F# sources changed
npm run bench   # vitest bench --run
```

### Results

Latest local run `npm run bench`, 30 side-by-side Vitest
benchmark groups. Averages below are the geometric mean of per-benchmark
throughput (`hz`) ratios, so each benchmark group contributes equally.

| Comparison | Average relative performance | Wins |
| ---------- | ---------------------------- | ---- |
| Klip vs `clipper2-ts` | `1.15x` as fast (`+14%`) | Klip faster in 27 / 30 groups |
| Klip vs `clipper2-wasm` | `0.63x` as fast (`-36%`) | Klip faster in 4 / 30 groups |
| `clipper2-wasm` vs `clipper2-ts` | `1.8x` as fast (`+81%`) | `clipper2-wasm` faster in 30 / 30 groups |

`clipper2-wasm` was the fastest implementation in 26 / 30 groups. Klip was
fastest on several small and fresh-instance cases, while the WebAssembly package
led most complex, reused-instance, and larger polygon cases.
