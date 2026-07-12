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
npm run bench    # vitest bench --run
cd ../..
```

Compiled to JS with Fable `Klip` is slightly faster than `clipper2-ts`, but still almost 2x slower than `clipper2-wasm`.

## Running F# Benchmarks

The .NET benchmark harness compares Clipper2 `2.0.0` with the local `Klip.fsproj`
for boolean clipping only: intersection, union, difference, and xor. Offsetting
and triangulation aren't exposed by Klip.

```bash
dotnet run -c Release --project Test/FSharp/Benchmark/Benchmark.csproj -- --join
```

Surprisingly Klip is 5% to 30 % faster than Clipper2 using the original Clipper2 benchmarks.


|                Method | EdgeCount |        Mean | Error |    StdDev |    Gen 0 |    Gen 1 |    Gen 2 |   Allocated |
|---------------------- |---------- |------------:|------:|----------:|---------:|---------:|---------:|------------:|
| Clipper2_Intersection |       100 |    767.1 us |    NA |   2.74 us |  23.4375 |   7.8125 |        - |    311.8 KB |
|     Klip_Intersection |       100 |    638.9 us |    NA |   6.14 us |  46.8750 |  15.6250 |        - |    591.3 KB |
|        Clipper2_Union |       100 |    914.8 us |    NA |  42.70 us |  15.6250 |        - |        - |   254.04 KB |
|            Klip_Union |       100 |    603.3 us |    NA |  25.23 us |  39.0625 |   7.8125 |        - |   507.51 KB |
|   Clipper2_Difference |       100 |    712.4 us |    NA |   0.11 us |  15.6250 |        - |        - |   283.83 KB |
|       Klip_Difference |       100 |    628.6 us |    NA |   8.67 us |  39.0625 |  15.6250 |        - |   546.44 KB |
|          Clipper2_Xor |       100 |    832.5 us |    NA |  12.21 us |  39.0625 |  15.6250 |        - |   503.63 KB |
|              Klip_Xor |       100 |    725.0 us |    NA |  15.41 us |  62.5000 |  31.2500 |        - |   796.88 KB |
| Clipper2_Intersection |       500 | 21,498.7 us |    NA | 111.93 us | 289.0625 | 273.4375 | 125.0000 |  3551.83 KB |
|     Klip_Intersection |       500 | 20,717.4 us |    NA |  11.92 us | 867.1875 | 812.5000 |        - | 10692.42 KB |
|        Clipper2_Union |       500 | 18,311.8 us |    NA | 422.10 us |  93.7500 |  62.5000 |        - |  1189.27 KB |
|            Klip_Union |       500 | 17,895.8 us |    NA |  41.48 us | 640.6250 | 585.9375 |        - |  7869.52 KB |
|   Clipper2_Difference |       500 | 20,985.8 us |    NA | 220.66 us | 203.1250 | 195.3125 |  46.8750 |  2060.38 KB |
|       Klip_Difference |       500 | 18,471.7 us |    NA |   3.46 us | 726.5625 | 687.5000 |        - |  8931.73 KB |
|          Clipper2_Xor |       500 | 22,552.5 us |    NA |  19.20 us | 343.7500 | 328.1250 | 148.4375 |  4355.15 KB |
|              Klip_Xor |       500 | 21,551.2 us |    NA | 748.48 us | 968.7500 | 929.6875 |        - | 11915.43 KB |




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

The F# tests round each solution's coordinates (`Helpers.roundPaths`, real `Math.Round`) before
asserting on areas/point counts, so they compare against the integer values the fixtures expect.

The tolerance-related behavior is split deliberately (the individual properties are
`[<Obsolete>]`-hidden expert overrides - the `Clipper64.Tolerance` property is the supported knob, and test
files that poke the individual properties carry `#nowarn "44"`):

- `Clipper64.CoordEqTolerance` controls near-equal coordinate comparisons.
- `Clipper64.ColinearityTolerance` controls cross-product colinearity checks.
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

