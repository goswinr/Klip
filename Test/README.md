# Klip - Test and benchmarks

## Running F# Tests

- adapted from the original C# tests in [Clipper2](https://github.com/AngusJohnson/Clipper2/tree/main/CSharp/Tests).
- additional test for Union of (almost) touching Polygons are added in `BooleanTests.fs` to cover the `MergeVertexTolerance` behavior described in `Engine2.fs`.

```bash
dotnet test Test/FSharp/Tests/Tests1/Tests1.fsproj
dotnet test Test/FSharp/Tests/Tests2/TestsZ.fsproj
```

## Running JS Tests

Vitest-based tests that exercise Klip's Fable output against the same fixtures used by the upstream
[clipper2-ts](https://github.com/countertype/clipper2-ts/tree/main/tests) test suite.
Tests run against the **already-compiled** JS output in `_dist/Klip.mjs`. If you
change the F# sources, rebuild first:

```bash
cd Test/TypeScript
dotnet tool restore
npm install     # install vitest and dependencies
cd ../..
```

Then run the tests with:

```bash
cd Test/TypeScript
npm run clean   # clean previous Fable output
npm run build   # dotnet fable + vite build
npm test        # vitest --run
cd ../..
```

Vitest config: `vitest.config.ts` - picks up `tests/**/*.{test,spec}.ts`,
excludes `_ts/fable_modules`.


## Running JS Benchmarks

See `TypeScript/bench/README.md` for details on the JS benchmarks.

```bash
cd Test/TypeScript
npm run build   # rebuild _dist/Klip.mjs if F# sources changed
npm run bench            # side-by-side Vitest operation benchmarks
npm run bench:scanline   # scanline-container threshold sweep
cd ../..
```

On the 2026-09-19 local run, Fable-compiled Klip was `1.32x` as fast as `clipper2-ts` in 29 of 30
benchmark groups, and `0.73x` as fast as `clipper2-wasm`. See
[`TypeScript/bench/README.md`](TypeScript/bench/README.md) for methodology and the full current summary.

## Running F# Benchmarks

The .NET benchmark harness compares Clipper2 `2.0.0` with the local `Klip.fsproj`
for boolean clipping only: intersection, union, difference, and xor. Offsetting
and triangulation aren't exposed by Klip.

```bash
dotnet run -c Release --project Test/FSharp/Benchmark/Benchmark.csproj -- --join
dotnet run -c Release --project Test/FSharp/Benchmark/Benchmark.csproj -- --filter '*VitestFixtureBenchmarks*'
```

The two suites characterize different workloads, so neither should be treated as the whole story:

- The dense random-polygon suite gave Klip a `1.31x` geometric-mean speed ratio across 8 pairs (6 wins).
  At 500 edges all four operations were 4-8% faster; at 100 edges, Difference and XOR were 2.76-2.97x
  faster while Intersection and Union were 7% and 14% slower.
- The 17 fixture-based cases gave Klip a `0.84x` geometric-mean speed ratio (about 16% slower), with one
  small-XOR win. The largest stable gaps are grid unions and small simple unions.
- Klip allocates about 1.6-9x more managed memory than Clipper2 in these runs. BenchmarkDotNet uses a
  short local configuration; compare ratios, not its single machine's absolute timings.

## What's covered

Klip currently exposes a subset of Clipper2's surface: polygon boolean ops,
PolyTree output, generic vertex metadata (`'Z`), path construction helpers, and
direct `Clipper64` access for open subjects. The convenience wrappers in
`Src/Klip.fs` include `booleanOp`, `intersect`, `union`, `unionSelf`,
`unionSelfChecked`, `difference`, `xor`, `removeSelfIntersectionsPositive`,
`removeSelfIntersectionsNegative`, `booleanOpPolyTree`, and `polyTreeToPaths64`.
The `KlipperZ` module mirrors these with a Z callback argument.

The TypeScript Vitest harness mirrors the boolean / PolyTree
fixtures, with an additional F# port under `FSharp/`:

| File                       | Mirrors                              | Notes                                                                      |
| -------------------------- | ------------------------------------ | -------------------------------------------------------------------------- |
| `tests/polygons.test.ts`   | `clipper2-ts/tests/polygons.test.ts` | All 195 Polygons.txt cases + PolyTree consistency, basic ops, edge cases   |
| `tests/polytree.test.ts`   | `clipper2-ts/tests/polytree.test.ts` | Hole ownership, complex nesting, area validation                           |
| `tests/sliver-triangle.test.ts` | `clipper2-ts/tests/sliver-triangle.test.ts` | Regression for Clipper2 issue #1067 - NonZero union over sliver triangles |
| `tests/test-data-parser.ts`| `clipper2-ts/tests/test-data-parser.ts` | Self-contained: defines local `ClipType`/`FillRule` enums and `Point64` shape |
| `tests/test-data/`         | `clipper2-ts/tests/test-data/`       | `Polygons.txt`, `PolytreeHoleOwner.txt`, `PolytreeHoleOwner2.txt` |
| `FSharp/Tests/Tests1/Tests/SliverTriangleTests.fs` | `clipper2-ts/tests/sliver-triangle.test.ts` | F# port of the issue #1067 regression |

Tests not ported to the TypeScript harness because Klip does not expose the
corresponding API: `offsets.test.ts` (polygon offsetting), `rectClip`,
`triangulation`, `minkowski`, `precision`, and `comprehensive`. Open-subject and
Z-callback behavior are covered by the F# test projects.

## Unrounded-float engine and test tolerances

Klip's engine runs on **unrounded `float` coordinates** ( see the
[main README](../README.md#coordinate-precision-unrounded-floats)). The `Polygons.txt` reference
counts/areas come from an integer-snapped clipper, so a handful of complex cases now resolve into a
slightly different number of (touching) contours. This is absorbed by raised per-case tolerances in
`tests/polygons.test.ts` - grep for `unrounded` - in the same spirit as the area tolerances that the
file already retunes for engine behavior. Test `181` needed a notably large count
allowance and is flagged in a comment as worth revisiting.

Each fixture runs twice: with the float defaults for area checks, and with explicit legacy
`NearTopYToleranceCap`, `SmallTriangleTolerance`, and `SplitAreaTolerance` values of `2.0` for
both count and area checks. The integer reference counts exclude small contours that the float
defaults now preserve. Count and area assertion bounds have not been widened for this change.

`GeometryToleranceTests.fs` and `tolerance-regressions.test.ts` exercise the actual geometry predicates
for boundary distance, winding invariance, and proper crossings. `ToleranceUnitTests.fs` also checks
coherent defaults and preservation of unit and subunit triangles without rounding their output.

The F# tests round each solution's coordinates (`Helpers.roundPaths`, real `Math.Round`) before
asserting on areas/point counts, so they compare against the integer values the fixtures expect.

The tolerance-related behavior is split deliberately (the individual properties are
`[<Obsolete>]`-hidden expert overrides - the `Clipper64.Tolerance` property is the supported knob, and test
files that poke the individual properties carry `#nowarn "44"`):

- `Clipper64.CoordEqTolerance` controls near-equal coordinates and point-on-boundary distance.
- `Clipper64.ColinearityTolerance` controls angular cleanup and joins; orientation signs and proper crossings do not use it.
- `Clipper64.MergeVertexTolerance` controls adjacent-edge join distance.
- `Snap.xAndY` / `Snap.xAndYSingle` are a standalone, opt-in pre-pass that cluster nearby input X and Y coordinates per-axis, mutating paths in place before clipping. The `Klipper.*` wrappers do not apply it automatically.
- Structural scanline ordering and vertex Y ordering remain exact; horizontal-edge detection uses `Clipper64.HorizontalAngleTolerance`.

The TypeScript Vitest suite is the main regression gate for clipping correctness because it runs the broad upstream fixture set against the compiled Fable bundle. Rebuild `_dist/Klip.mjs` with `npm run build` before running `npm test` after F# source changes.


### Exploratory scripts

The `CrossProductSignCompare.fsx` script is a standalone comparison harness for
`CrossProductSign`. It compares three orientation-sign calculations for large,
integer-stepped `Point64` values:

- the Clipper-style integer-product implementation
- a direct `float64` determinant sign
- a `BigDecimal` determinant sign via `ExtendedNumerics.BigDecimal`

