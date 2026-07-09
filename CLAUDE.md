# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Klip is a partial F# port of [Clipper2](https://github.com/AngusJohnson/Clipper2) for fast, robust polygon
boolean clipping (intersection, union, difference, XOR + PolyTree output). It is derived from the
[clipper2-ts](https://github.com/countertype/clipper2-ts) TypeScript port, not the original C#. The library
targets `netstandard2.0` and is also compiled to JavaScript/TypeScript via [Fable](https://fable.io/), so it
runs on .NET and in the browser from one source.

Scope is intentionally limited to polygon boolean ops — there is **no** offsetting, line/rect clipping,
Minkowski sums, triangulation, or int64 API.

## Build and test

```bash
dotnet build
cd Test
dotnet tool restore
npm install
npm run clean   # clean previous Fable output
npm run build  # dotnet fable → _js, then vite build → _dist/Klip.mjs
dotnet test FSharp/Tests/Tests1/Tests1.fsproj   # boolean ops, open paths, PolyTree
dotnet test FSharp/Tests/Tests2/TestsZ.fsproj   # Z-callback metadata wiring
npm test # vitest --run  (imports the compiled _dist/Klip.mjs)
cd ..
```

Run a single vitest file/case from `Test/`: `npx vitest run tests/polygons.test.ts -t "name"`.

The Fable toolchain is pinned in `Test/dotnet-tools.json`; `npm install` runs
`dotnet tool restore` via the `preinstall` script.

JS tests run against the
**already-compiled** `_dist/Klip.mjs` — after editing any `Src/*.fs` you must `npm run build` before
`npm test`, or you are testing stale output.

## Source architecture (`Src/`, compiled in this order)

The six files compile in this order, each depending only on the ones before it (F# compiles
top-to-bottom; order in `Klip.fsproj` matters):

1. **`Core.fs`** — namespace `Klip`. Public coordinate types plus internal helpers:
   - `Path64<'Z>` — a single contour. **XY coordinates live in one flat interleaved `ResizeArray<float>`**
     as `x0,y0,x1,y1,…` (not a list of points). Optional parallel `zs` array holds `'Z` metadata.
   - `Paths64<'Z>` — `ResizeArray<Path64<'Z>>` (outer contours + holes).
   - `module Geo` — geometry primitives (signed area, cross products, point-in-polygon).
   - Internal `Null`, `Rarr`, `Operators` modules contain Fable-specific fast paths guarded by
     `#if FABLE_COMPILER_JAVASCRIPT / _TYPESCRIPT` (e.g. reading `.length`/`null` directly to avoid
     Fable library calls). Preserve these conditionals when editing.

2. **`Snap.fs`** — `module Snap`, standalone per-axis input pre-snapping (depends only on `Core.fs`,
   not on the engine). Collects near-vertical / near-horizontal segment runs and snaps each per-axis
   cluster to its mean, in place. A coarse pre-pass to run before clipping noisy inputs; the fine,
   in-sweep counterpart is `Clipper64.CoordEqTolerance`.

3. **`KlipInternalTypes.fs`** — `ClipType` / `PathType` / `FillRule` enums and `module KlipInternalTypes`:
   the internal working types (`Vertex`, `LocalMinima`, `OutPt`, `OutRec`, `Active`/`ActiveEdge`,
   `HorzSegment`, `HorzJoin`, `IntersectNode`, `PolyPath64<'Z>`, `PolyTree64<'Z>`, the two
   pending-scanline containers `ScanlineArray` (linear scan, small jobs) / `ScanlineHeapSet`
   (max-heap + dedup set, large jobs; switch-over size is `Clipper64.ScanlineArrayThreshold`,
   default via `Klipper.setDefaultScanlineArrayThreshold`, benchmarked by
   `Test/bench/scanline-threshold.mjs`), plus the `VertexFlags` / `JoinWith` / `HorzPosition`
   flag types). Types only — comparisons here are exact ordering on scanline Y.

4. **`EngineUtil.fs`** — `module internal Eng`: the stateless engine helpers split out of the clipping
   class — geometry/edge primitives (`getLineIntersectPt`, `topX`, `getMaximaPair`, …) and the
   scale-free predicates `isHorizontalCoords` / `getDx` / `isHorizontal`. These take tolerances as
   explicit arguments rather than reading per-instance state.

5. **`Engine.fs`** — the actual vatti-style sweep-line clipping engine: the public **`Clipper64<'Z>`**
   class with `AddSubject` / `AddOpenSubject` / `AddClip` / `Execute`. The per-instance,
   tolerance-dependent helpers (`isHorizontal`, `checkJoinLeft` / `checkJoinRight`, …) are `let`-bound
   inside the class body so they close over the instance's mutable tolerances. This is the largest file
   and the core algorithm.

6. **`Klip.fs`** — `module Klipper`, the high-level convenience API (`intersect`, `union`, `unionSelf`,
   `unionSelfChecked`, `difference`, `xor`, `removeSelfIntersectionsPositive`,
   `removeSelfIntersectionsNegative`, `booleanOp`, `booleanOpPolyTree`, `polyTreeToPaths64`), each with a `…Z`
   variant taking a `ZCallback64<'Z>`. These wrappers always treat input as **closed** polygons.

### Two key design points unique to this port

- **Generic `'Z` metadata.** Unlike Clipper2's fixed `int64` Z, Klip's `'Z` is a user-defined type
  attached to vertices, defaulting to `unit`. The no-Z helpers (`Path64.createFrom`,
  `Paths64.createSingle`) produce `…<unit>` values. `'Z` is metadata, **not** a 3rd coordinate.

- **Unrounded float coordinates.** The engine computes on raw `float`s —
  (no integer snapping). Consequences you must respect when touching the engine:

  - Point coincidence / colinearity use **tolerances**, not exact equality: `Geo.coordEq`/`coordNeq`
    (`coordEqTol = 1e-5`) for coordinates and `Clipper64.ColinearityTolerance` (default `1e-3`) for
    cross-product colinearity. Route coordinate comparisons through these.

  - Adjacent touching-edge joins must not use fixed integer-grid windows. The `checkJoinLeft` /
    `checkJoinRight` near-top guard is edge-height-relative and capped at `2.0`, while
    `Clipper64.MergeVertexTolerance` controls the perpendicular join-distance tolerance
    (default `Geo.coordEqTol`) for noisy or very tiny touching edges.

  - **Sort comparators and structural vertex-Y / scanline ordering stay exact** — relaxing those
    breaks the scanbeam. Point-equality, colinearity, and *horizontality* are tolerance-based.
    `isHorizontal` uses a tight scale-relative angle test (`Eng.isHorizontalCoords`:
    `|Δy| <= horzAngleTol * |Δx|`, default `1e-6`) coupled to `Eng.getDx`, so an unrounded
    shared near-horizontal edge (e.g. a top edge at `37` vs `37.00000000000001`) is treated as
    horizontal instead of landing its ends on two distinct scanlines and sealing an open notch
    into a phantom hole. Keep `getDx` and `isHorizontal` routed through the same predicate.

  - Contours sharing a *seam* must merge, not stay separate: horizontal seams join via
    `convertHorzSegsToJoins` (strict X-range overlap — a real seam's overlap dwarfs float noise);
    sloped/near-vertical seams join via `checkJoinLeft`/`checkJoinRight` gated by
    `MergeVertexTolerance`. Contours touching at a single *point* (e.g. XOR lobes) stay separate
    by design, as in Clipper2 — do **not** join zero-length-overlap horizontal segments; that
    pinches valid output into a figure-8 and breaks the join op-walk termination.

  - The engine does **not** normalize coordinate magnitude. All absolute tolerances
    (`CoordEqTolerance`, `MergeVertexTolerance`, `NearTopYToleranceCap`, `SmallTriangleTolerance`,
    and the area-valued `SplitAreaTolerance`) are per-instance and must be scaled by the *caller*
    to the input's coordinate magnitude — individually, or all five at once from one length via
    `Clipper64.SetToleranceUnit` (unit `1.0` = the defaults; `SplitAreaTolerance` scales with the
    unit *squared*); the angle tolerances (`ColinearityTolerance`, `HorizontalAngleTolerance`)
    are scale-free. For integer output, round solution coords yourself after clipping.

## Open vs closed paths

Open/closed is **not** inferred from coordinates — it is set when a path is added. The `Klipper.*`
wrappers always treat input as closed. To clip open polylines, drop to `Clipper64` directly and call
`AddOpenSubject` (clip paths are always closed). `Execute` can take separate closed and open solution
outputs. See `README.md` for the full rule set.

## Tests layout

- `Test/tests/*.test.ts` — vitest suite . `tests/adapter.ts` converts between
  clipper2-ts `{x,y}[]` fixtures and Klip's flat-buffer `Path64` shape, and re-implements `area` /
  `pointInPolygon` / PolyTree helpers locally because the compiled bundle tree-shakes those members
  off (only the exported boolean ops survive). `tests/test-data/` holds the upstream `.txt` fixtures.

  Some `polygons.test.ts` cases have **raised tolerances** for unrounded-mode divergence (grep
  `unrounded`); test 181 needs a notably large count allowance and is flagged for revisiting.

- `Test/FSharp/Tests/Tests1` & `Tests2` — F# ports; reference `Klip.fsproj` directly. They round
  solution coords (`Helpers.roundPaths`) before asserting so they match the integer-snapped fixtures.
- `Test/FSharp/Benchmark` — BenchmarkDotNet vs Clipper2 2.0.0 (closed boolean ops only):
  `dotnet run -c Release --project FSharp/Benchmark/Benchmark.csproj -- --join`. `Benchmarks.cs` uses a
  dense random-polygon dataset (`[Params] EdgeCount`), where nearly every scanbeam intersects.
  `VitestFixtureBenchmarks.cs` instead mirrors the shapes and overlap pairs from
  `Test/bench/test-data.ts` / `clipping-operations.bench.ts` (the JS vitest bench suite), giving a
  second, less intersection-dense comparison point against the same Clipper2 NuGet package; run with
  `dotnet run -c Release --project FSharp/Benchmark/Benchmark.csproj -- --filter '*VitestFixtureBenchmarks*'`
  (uses BenchmarkDotNet's default adaptive job rather than `FastConfig`'s fixed invocation count, since
  most of these fixtures are too cheap for a fixed count to produce a stable measurement).

- `Test/Rhino/*.fsx` and `Test/FSharp/Tests/*.fsx` — exploratory `dotnet fsi` scripts (Rhino drawing,
  cross-product-sign comparison); not part of CI.

## Conventions

- `Klip.fsproj` builds with `WarningLevel 5` plus `--warnon:3390` (XML docstring verification) and
  `--warnon:1182` (unused variables). Public API needs `///` XML docs; keep builds warning-clean.

- Packaging is automatic (`GeneratePackageOnBuild`), version comes from `CHANGELOG.md` via
  Ionide.KeepAChangelog — add a changelog entry rather than editing `<Version>`.

- Source files are also packed for Fable consumers (`**/*.fs` → `fable/`), so they must stay
  Fable-compilable; mind the `#if FABLE_COMPILER*` branches.
