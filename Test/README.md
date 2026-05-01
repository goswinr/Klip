# Klip - TypeScript test harness

Vitest-based tests that exercise Klip's Fable-compiled TypeScript output (`./_ts/Src/`)
against the same fixtures used by the upstream
[clipper2-ts](https://github.com/countertype/clipper2-ts) test suite.

## What's covered

Klip currently exposes only a subset of Clipper2's surface - the boolean ops and
PolyTree wrappers in `Klip/Src/Klip.fs` (`booleanOp`, `intersect`, `union`,
`unionSelf`, `difference`, `xor`, `booleanOpWithPolyTree`, `polyTreeToPaths64`).
The applicable upstream tests are mirrored here:

| File                       | Mirrors                              | Notes                                                                      |
| -------------------------- | ------------------------------------ | -------------------------------------------------------------------------- |
| `tests/polygons.test.ts`   | `clipper2-ts/tests/polygons.test.ts` | All 195 Polygons.txt cases + PolyTree consistency, basic ops, edge cases   |
| `tests/polytree.test.ts`   | `clipper2-ts/tests/polytree.test.ts` | Hole ownership, complex nesting, area validation                           |
| `tests/test-data-parser.ts`| `clipper2-ts/tests/test-data-parser.ts` | Self-contained: defines local `ClipType`/`FillRule` enums and `Point64` shape |
| `tests/test-data/`         | `clipper2-ts/tests/test-data/`       | `Polygons.txt`, `PolytreeHoleOwner.txt`, `PolytreeHoleOwner2.txt`          |

Tests not ported (Klip doesn't expose the corresponding API): `lines.test.ts`
(open subjects), `offsets`, `rectClip`, `triangulation`, `minkowski`,
`precision`, `z-callback`, `sliver-triangle`, `comprehensive`.

## Format adapter

Klip's `Path64` is a class with parallel `xs[]/ys[]/zs[]` buffers, while
clipper2-ts (and the test fixtures) use `{x, y}[]`. `tests/adapter.ts`
converts between the two and provides Klip-flavoured `area` / `areaPaths`
helpers (delegating to Klip's `Geo_area`).

The test data parser keeps producing clipper2-ts-style `{x, y}[]` paths;
conversion to Klip's `Path64` happens at the call site via the adapter.

## Running

Tests run against the **already-compiled** TS output in `./_ts/Src/`. If you
change the F# sources, rebuild first:

```bash
npm run buildTS   # dotnet fable ../Klip.fsproj --outDir ./_ts --lang ts --run tsc
npm test          # vitest --run
```

Or watch mode:

```bash
npm run test:watch
```

Vitest config: `vitest.config.ts` - picks up `tests/**/*.{test,spec}.ts`,
excludes `_ts/fable_modules`.

## Benchmarks

`bench/` ports the applicable benchmarks from
[clipper2-ts/bench/](https://github.com/countertype/clipper2-ts/tree/main/bench) (skipping offset, inflate, and
triangulation, which Klip doesn't expose). Each `describe` group times the same
operation against:

- **clipper2-ts** - imported from the published
  [`clipper2-ts`](https://www.npmjs.com/package/clipper2-ts) npm package, not
  the local source
- **Klip** - imported from `_dist/Klip.mjs` (the production Vite bundle)

| File                                 | Notes                                                         |
| ------------------------------------ | ------------------------------------------------------------- |
| `bench/bench-stats.ts`               | Copied verbatim from clipper2-ts                              |
| `bench/test-data.ts`                 | Copy with import switched to `clipper2-ts` npm                |
| `bench/lip-helpers.ts`               | Duck-typed `{xs, ys, zs}` adapter and Klip ops re-export       |
| `bench/clipping-operations.bench.ts` | Side-by-side clipper2-ts vs Klip benches                       |

Klip inputs are pre-converted to its parallel-buffer `Path64` shape outside the
timed regions (mirroring how clipper2-ts excludes input setup). The adapter
duck-types `Path64` and `PolyTree64` instead of importing the classes, so
`_dist/Klip.mjs` doesn't need to expose internals.

Run:

```bash
npm run buildJS   # rebuild _dist/Klip.mjs if F# sources changed
npm run bench     # vitest bench --run
```

### Latest results

Numbers below are throughput in operations/second (higher is better) from a
single `npm run bench` run on Windows + Node. Treat sub-1.05x ratios as noise.

| Operation                                        | clipper2-ts (hz) |     Klip (hz) | Ratio        |
| ------------------------------------------------ | ---------------: | -----------: | ------------ |
| union - medium complex polygon                   |          140,342 |      151,347 | **Klip 1.08x**|
| union - large complex polygon                    |           30,334 |       31,223 | Klip 1.03x    |
| union - very large complex polygon (2000 verts)  |            7,503 |        6,660 | c2ts 1.13x   |
| union - medium grid (25 rectangles)              |           56,945 |       60,439 | Klip 1.06x    |
| union - large grid (100 rectangles)              |           15,399 |       15,620 | Klip 1.01x    |
| intersection - medium overlapping polygons       |           68,898 |       63,761 | c2ts 1.08x   |
| intersection - large overlapping polygons        |           17,241 |       16,553 | c2ts 1.04x   |
| intersection - grid with rectangle               |           63,752 |       68,451 | Klip 1.07x    |
| difference - medium overlapping polygons         |           68,115 |       65,775 | c2ts 1.04x   |
| difference - large overlapping polygons          |           16,571 |       15,982 | c2ts 1.04x   |
| xor - medium overlapping polygons                |           55,688 |       58,123 | Klip 1.04x    |
| xor - large overlapping polygons                 |           13,453 |       13,235 | c2ts 1.02x   |
| 10x union on medium polygons                     |           14,045 |       13,939 | c2ts 1.01x   |
| 10x intersection on medium polygons              |            6,898 |        6,831 | c2ts 1.01x   |
| convenience union - medium grid                  |           59,241 |       61,846 | Klip 1.04x    |
| convenience intersect - medium overlapping       |           70,948 |       71,092 | tie          |
| convenience difference - medium overlapping      |           66,427 |       67,354 | Klip 1.01x    |
| union - 3 non-overlapping rectangles             |          381,868 |      404,247 | Klip 1.06x    |
| union - 2 overlapping rectangles                 |          472,870 |      560,550 | **Klip 1.19x**|
| union - 4 simple circles (no self-intersection)  |          267,416 |      294,569 | **Klip 1.10x**|
| polytree - medium grid union                     |           53,164 |       56,991 | Klip 1.07x    |
| polytree - nested rectangles (difference)        |          478,881 |      469,689 | c2ts 1.02x   |
| polytree - complex overlapping (intersection)    |           67,214 |       66,068 | c2ts 1.02x   |
| union - geo-scale complex polygon                |          110,753 |      136,658 | **Klip 1.23x**|
| intersection - geo-scale overlapping             |           49,381 |       69,107 | **Klip 1.40x**|

Klip wins clearly on simple shapes, geo-scale (large) coordinates, and small
polytree workloads. clipper2-ts is faster on the 2000-vertex single-polygon
union and on medium/large pairwise intersection/difference; everywhere else
the two are within ±5%.

