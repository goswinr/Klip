# Klip - TypeScript test and benchmark harness

Vitest-based tests that exercise Klip's Fable output against the same fixtures used by the upstream
[clipper2-ts](https://github.com/countertype/clipper2-ts/tree/main/tests) test suite.



## What's covered

Klip currently exposes a subset of Clipper2's surface: the boolean ops and
PolyTree wrappers in `Klip/Src/Klip.fs`
(`booleanOp`, `intersect`, `union`, `unionSelf`, `difference`, `xor`,
`booleanOpWithPolyTree`, `polyTreeToPaths64`).

The TypeScript Vitest harness mirrors the boolean / PolyTree
fixtures, with an additional F# port under `FSharp/`:

| File                       | Mirrors                              | Notes                                                                      |
| -------------------------- | ------------------------------------ | -------------------------------------------------------------------------- |
| `tests/polygons.test.ts`   | `clipper2-ts/tests/polygons.test.ts` | All 195 Polygons.txt cases + PolyTree consistency, basic ops, edge cases   |
| `tests/polytree.test.ts`   | `clipper2-ts/tests/polytree.test.ts` | Hole ownership, complex nesting, area validation                           |
| `tests/sliver-triangle.test.ts` | `clipper2-ts/tests/sliver-triangle.test.ts` | Regression for Clipper2 issue #1067 — NonZero union over sliver triangles |
| `tests/test-data-parser.ts`| `clipper2-ts/tests/test-data-parser.ts` | Self-contained: defines local `ClipType`/`FillRule` enums and `Point64` shape |
| `tests/test-data/`         | `clipper2-ts/tests/test-data/`       | `Polygons.txt`, `PolytreeHoleOwner.txt`, `PolytreeHoleOwner2.txt` |
| `FSharp/Tests/Tests1/Tests/SliverTriangleTests.fs` | `clipper2-ts/tests/sliver-triangle.test.ts` | F# port of the issue #1067 regression |

Tests not ported (Klip doesn't expose the corresponding API): `offsets.test.ts`
(polygon offsetting), `lines.test.ts` (open subjects), `rectClip`,
`triangulation`, `minkowski`, `precision`, `z-callback`, `comprehensive`.

## Unrounded-float engine and test tolerances

Klip's engine runs on **unrounded `float` coordinates** (`Geo.jsRound` is the identity — see the
[main README](../README.md#coordinate-precision-unrounded-floats)). The `Polygons.txt` reference
counts/areas come from an integer-snapped clipper, so a handful of complex cases now resolve into a
slightly different number of (touching) contours. This is absorbed by raised per-case tolerances in
`tests/polygons.test.ts` — grep for `unrounded` — in the same spirit as the area tolerances that the
file already retunes for engine behavior. Test `181` needed a notably large count
allowance and is flagged in a comment as worth revisiting.

The F# tests round each solution's coordinates (`Helpers.roundPaths`, real `Math.Round`) before
asserting on areas/point counts, so they compare against the integer values the fixtures expect.


### What changed
Src/Core.fs — the Geo primitives:

jsRound now returns its input unchanged (no more Math.Truncate/Math.trunc) — the engine computes on unrounded floats.
New coordEq/coordNeq with coordEqTol = 1e-6 replace exact coordinate equality.
New crossIsZero (relative tolerance crossCollinearRelTol = 1e-12) used by crossProductSign and productsAreEqual/isCollinear.
pointInPolygon coordinate comparisons use coordEq.
Src/Engine2.fs — xyEqual/xyNotEqual, the now-fractional curX comparisons, and the point-in-polygon / horizontal-segment coincidence checks all route through Geo.coordEq/coordNeq. (Sort comparators, structural vertex-Y/scanline logic, and isHorizontal stay exact — relaxing those breaks the scanbeam.)

Src/Engine1.fs — no changes needed (types + ScanlineHeap only; its comparisons are ordering on exact vertex-Y scanlines).

Tests — F# tests round solution coords via a new Helpers.roundPaths before asserting; polygons.test.ts count/area tolerances were retuned for the cases where unrounded geometry legitimately diverges (clearly commented as "unrounded mode").

### The important detour
Your literal spec (abs(a-b) < 1.0) and the F# unit tests alone were misleading — those 22 tests passed, but the repo's 203-case TypeScript suite regressed 203→175. Investigating showed:

1.0 is too coarse for this library's typical coordinate magnitudes (~10–1000) — it fused distinct points and doubled the damage. A tiny tolerance (1e-6) behaves ≈exact here while still absorbing float noise.
Collinearity is cross-product based, not coordinate-equality based, so it also needed a tolerance (crossIsZero) to keep collinear edges merging.
A residual handful of complex cases inherently fragment a bit differently without integer snapping (test 181 by ~37); that can't be undone at output time, so it's absorbed by retuned suite tolerances.
### Verification
Suite	Result
.NET library build	clean (0 warnings/errors)
Fable JS transpile + bundle	clean
F# Tests1 / Tests2	15/15, 7/7
TS polygons + polytree + sliver	203/203
I also saved two memory notes for future work here: that the TS vitest suite (not the F# tests) is the real correctness gate and how to run it, and the rationale behind the unrounded-float tolerances.

One thing worth flagging: polygons.test.ts test 181 now needs a large count allowance (≤40). I papered over it per your "retune" decision, but a +37 polygon divergence is big enough that it may be a specific pathology worth a real look later — I left a comment marking it.

## Format adapter

Klip's `Path64` is a class with one flat interleaved `XYs` buffer, while
clipper2-ts (and the test fixtures) use `{x, y}[]`. `tests/adapter.ts`
converts between the two and provides local `area` / `areaPaths` helpers
that re-implement the same shoelace logic as Klip's `Path64.SignedArea`
member (Core.fs:234) — needed because Klip's compiled bundle tree-shakes the
member off when no exported function references it.

The test data parser keeps producing clipper2-ts-style `{x, y}[]` paths;
conversion to Klip's `Path64` happens at the call site via the adapter.

## Running F# Tests

adapted from the original C# tests in [Clipper2](https://github.com/AngusJohnson/Clipper2/tree/main/CSharp/Tests).


```bash
cd Test
dotnet test FSharp/Tests/Tests1/Tests1.fsproj
dotnet test FSharp/Tests/Tests2/TestsZ.fsproj
```

## Running JS Tests

Tests run against the **already-compiled** JS output in `_dist/Klip.mjs`. If you
change the F# sources, rebuild first:

```bash
cd Test
npm install     # install vitest and dependencies
```

Then run the tests with:

```bash
cd Test
npm run build   # dotnet fable + vite build
npm test        # vitest --run
```

Vitest config: `vitest.config.ts` - picks up `tests/**/*.{test,spec}.ts`,
excludes `_ts/fable_modules`.

### Run a specific test project

| Project | What it covers |
|---------|----------------|
| `Tests1` | Boolean clipping (union/intersect/difference/xor), open paths, PolyTree |
| `Tests2` (TestsZ) | Z-callback wiring on `Clipper64<'Z>` |

Both test projects target the `Klip` library directly via `..\..\..\..\Klip.fsproj`.
PolyTree-from-file tests from the original C# port are intentionally omitted —
Klip does not ship the upstream test-data files for those scenarios.

## Running JS Benchmarks

See `Test/bench/README.md` for details on the JS benchmarks.

```bash
cd Test
npm run build   # rebuild _dist/Klip.mjs if F# sources changed
npm run bench    # vitest bench --run
```

## Running F# Benchmarks

The .NET benchmark harness compares Clipper2 `2.0.0` with the local `Klip.fsproj`
for boolean clipping only: intersection, union, difference, and xor. Offsetting
and triangulation aren't exposed by Klip.

```bash
cd Test
dotnet run -c Release --project FSharp/Benchmark/Benchmark.csproj -- --join
```

Local run notes from 2026-05-07:

- BenchmarkDotNet `0.12.1`, .NET `10.0`, Windows `10.0.26200`, Intel Core i5-14600.
- Quick benchmark config is enabled: throughput strategy, 1 launch, 8 warmups,
  4 measured iterations, 256 invocations, and unroll factor 4. Treat the
  numbers as local comparison signals, not publication-grade statistics.
- Tested random closed subject/clip paths at `EdgeCount` 100 by default, with
  larger counts left commented in `Benchmarks.cs` for longer exploratory runs.
  The same generated coordinates are converted into Klip's flat
  `Path64<'Z>` buffer format.
- The full benchmark run completed in about `25.8s`.

| Operation | Clipper2 mean | Klip mean | Klip / Clipper2 | Clipper2 allocated | Klip allocated | Klip alloc ratio |
|-----------|--------------:|----------:|----------------:|-------------------:|---------------:|-----------------:|
| Intersection | `673.4 us` | `663.0 us` | `0.98x` | `311.8 KB` | `581.89 KB` | `1.87x` |
| Union | `620.4 us` | `617.3 us` | `0.99x` | `254.04 KB` | `493.91 KB` | `1.94x` |
| Difference | `634.7 us` | `631.5 us` | `0.99x` | `283.83 KB` | `535.18 KB` | `1.89x` |
| Xor | `741.0 us` | `710.6 us` | `0.96x` | `503.63 KB` | `778.48 KB` | `1.55x` |

- In this run, Klip's runtime was effectively tied with Clipper2 across the
  boolean-operation pairs, with measured means between `0.96x` and `0.99x` of
  Clipper2's means. The error bars overlap, so read that as parity rather than
  a clear speed win.
- Managed allocation remains higher for Klip in this C# harness: about `1.55x`
  to `1.94x` Clipper2's allocation, averaging roughly `1.8x` more.
- BenchmarkDotNet wrote the full reports under `../BenchmarkDotNet.Artifacts/`
  when run from this `Test` directory, or `BenchmarkDotNet.Artifacts/` when run
  from the repository root.


### Exploratory scripts

The `CrossProductSignCompare.fsx` script is a standalone comparison harness for
`CrossProductSign`. It compares three orientation-sign calculations for large,
integer-stepped `Point64` values:

- the Clipper-style integer-product implementation
- a direct `float64` determinant sign
- a `BigDecimal` determinant sign via `ExtendedNumerics.BigDecimal`

Run it with:

```bash
cd Test
dotnet fsi FSharp/Tests/CrossProductSignCompare.fsx
```
