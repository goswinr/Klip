# Klip - TypeScript test and benchmark harness

Vitest-based tests that exercise Klip's Fable output against the same fixtures used by the upstream
[clipper2-ts](https://github.com/countertype/clipper2-ts/tree/main/tests) test suite.



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

Klip's `Path64` is a class with one flat interleaved `coords[]` buffer, while
clipper2-ts (and the test fixtures) use `{x, y}[]`. `tests/adapter.ts`
converts between the two and provides Klip-flavoured `area` / `areaPaths`
helpers (delegating to Klip's `Geo_area`).

The test data parser keeps producing clipper2-ts-style `{x, y}[]` paths;
conversion to Klip's `Path64` happens at the call site via the adapter.

## Running F# Tests

adapted from the original C# tests in [Clipper2](https://github.com/AngusJohnson/Clipper2/tree/main/CSharp/Tests).


```bash
dotnet test Test/FSharp/Tests/Tests1/Tests1.fsproj
dotnet test Test/FSharp/Tests/Tests2/TestsZ.fsproj
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
Offsetting and PolyTree-from-file tests from the original C# port are intentionally
omitted — Klip does not expose those APIs.

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
and triangulation are intentionally skipped because Klip exposes only a subset of
Clipper2's API.

```bash
dotnet run -c Release --project Test/FSharp/Benchmark/Benchmark.csproj -- --join
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
dotnet fsi FSharp/Tests/CrossProductSignCompare.fsx
```
