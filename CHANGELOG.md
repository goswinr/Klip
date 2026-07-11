# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [3.1.0] - 2026-07-12

### Changed
- `Engine`: ported the sweep-loop optimizations from [clipper2-ts#34](https://github.com/countertype/clipper2-ts/pull/34) - skip the intersection merge sort on scanbeams where the active edge list is already sorted by `curX` (no intersections possible), and reuse the edge positions computed during that scan in `doTopOfScanbeam`; `isHorizontal` now reads the already-maintained `ae.dx` (±infinity if horizontal, see `Eng.getDx`) instead of re-testing the angle from bot/top coordinates; `checkJoinLeft`/`checkJoinRight` check the cheap `curX` mismatch before the hot/horizontal/open edge state; `Eng.boundingBoxesOverlap` early-exits on the first separating axis instead of computing all eight min/max values up front; `convertHorzSegsToJoins` compacts valid horizontal segments to the front of the list before sorting instead of sorting (and re-testing) invalid ones too; `addPathsToVertexList` now stores only each path's head vertex in `vertexList` (the rest of the chain stays reachable through the `next`/`prev` links), cutting one array slot per vertex. No change in clipping results - verified against the full F# (`Tests1`, `Tests2`) and JS (`vitest`) suites. The JS benchmark suite (`Test/bench/clipping-operations.bench.ts`) shows most intersection/difference/xor/union cases 10-40% faster, in line with the upstream PR's reported gains. Added `Test/FSharp/Benchmark/VitestFixtureBenchmarks.cs`, a BenchmarkDotNet suite comparing Klip against the Clipper2 2.0.0 NuGet package on those same fixture shapes (rather than `Benchmarks.cs`'s dense random-polygon dataset): on complex-polygon and geo-scale cases Klip is within ~1-6% of Clipper2 either way (noise level), but ~12-25% slower on grid/many-small-paths cases (`Union_MediumGrid`, `Union_LargeGrid`, `Intersection_Grid`, `Union_SimpleRects`/`TwoOverlapping`/`FourCircles`) - a divergence not visible in `Benchmarks.cs`'s single-large-path dataset, worth investigating separately.


### Added
- The `Clipper64.Tolerance` get/set property sets all five scale-dependent tolerances from a single absolute tolerance - the distance below which points are considered identical and lines touching: `CoordEqTolerance` = `MergeVertexTolerance` = `NearTopYToleranceCap` = `SmallTriangleTolerance` = the given tolerance, and the area-valued `SplitAreaTolerance` = the tolerance squared. The value is used as-is (valid range `0.0` .. `1e12`; `0` makes all five comparisons exact), **not** as a multiplier of the engine defaults - those (`1e-5` for the point tolerances, `2.0` for the culls) are calibrated for integer-Clipper2-style inputs of coordinate magnitude ~1e6 and do not correspond to any single value of this property. The dimensionless tolerances (`ColinearityTolerance`, `HorizontalAngleTolerance`, `NearTopYToleranceFactor`) are scale-invariant and untouched. Every tolerance comparison in the engine is dimensionally homogeneous, so clipping is scale-equivariant: scaling all input coordinates by `s` together with the tolerance yields the identically scaled solution - bit-exact for power-of-two `s`, verified by the new `ToleranceUnitTests`. The individual tolerance properties (`CoordEqTolerance`, `MergeVertexTolerance`, `NearTopYToleranceCap`, `SmallTriangleTolerance`, `SplitAreaTolerance`, and the dimensionless `ColinearityTolerance`, `HorizontalAngleTolerance`, `NearTopYToleranceFactor`) remain functional as expert overrides (e.g. raising `MergeVertexTolerance` above the seam gap of noisy inputs, or zeroing the sliver culls) but are now marked `[<Obsolete>]` to hide them from editor completion - the `Tolerance` property is the supported tuning surface; their valid ranges widen to `1e12` for the distances and `1e24` for `SplitAreaTolerance` to match.

## [3.0.0] - 2026-06-13

### Added
- `Clipper64.ScanlineArrayThreshold` exposes the size at which the engine switches its pending-scanline container from a small unsorted array (linear scan) to a max-heap plus hash-set, as a **per-instance** setting; `Klipper.setDefaultScanlineArrayThreshold` / `getDefaultScanlineArrayThreshold` adjust the process-wide default (64) used by new instances, including the ones the `Klipper`/`KlipperZ` wrappers create. Performance tuning only - the value never changes clipping results. The two containers are now proper classes (`ScanlineArray` / `ScanlineHeapSet`), replacing the previous inline array/heap/`HashSet` trio, and the formerly inconsistent switch sizes (start with the array at ≤ 16 local minima but only upgrade away from it past 64 pending scanlines) are unified into this single threshold. `Test/bench/scanline-threshold.mjs` benchmarks the switch-over in JS on two workloads (all-distinct vs duplicate-heavy scanline Ys): the array/heap crossover is broad and flat (~32–256 local minima), confirming 64; a set-free dedup-on-pop heap variant was also measured and rejected (up to ~18 % slower on duplicate-heavy inputs).

- `Clipper64.SmallTriangleTolerance` exposes the previously hard-coded `2.0` sliver-triangle cull window (a 3-point solution ring is dropped when two of its vertices are closer than this in both X and Y) as a **per-instance** setting. Default unchanged at `2.0`; like the other absolute tolerances it should be scaled by the caller to the coordinate magnitude of the input.
- `Clipper64.SplitAreaTolerance` exposes the previously hard-coded `4.0`/`2.0` double-area thresholds of the self-intersection split (`doSplitOp`) as a **per-instance** setting: a ring is discarded below this area, and a split-off triangle is kept only above half this area. Default unchanged at `2.0`; being an area it scales with the **square** of the coordinate magnitude.
- `Clipper64.CoordEqTolerance` exposes the in-sweep coordinate-equality tolerance as an adjustable, **per-instance** setting (default `1e-5`), independent of `MergeVertexTolerance`.
- `Clipper64.NearTopYToleranceFactor` and `NearTopYToleranceCap` expose the previously hard-coded constants of the adjacent-edge join near-top guard. Defaults unchanged at `1e-4` and `2.0`.
- `Clipper64.HorizontalAngleTolerance` exposes the scale-relative slope tolerance for treating an edge as horizontal (`abs Δy <= tol * abs Δx`), as a **per-instance** setting. Default `1e-6`; set `0` for the former exact `topY = botY` behaviour.

### Changed
- All hot-loop `ResizeArray` indexing (engine sweep, `Geo.pointInPolygon` and area/containment primitives, `Snap`, the scanline containers, the `Klipper` path helpers) now goes through the `Rarr.getIdx`/`setIdx` emit helpers, which compile to direct `arr[i]` under Fable instead of the bounds-checked fable-library `item()`/`setItem()` calls (a function call per element access). No JS-measurable change on the boolean-op benchmark (its hot core is linked-list traversal), but it removes per-access overhead from `pointInPolygon`-heavy polytree workloads and `Snap`. Behaviour unchanged on .NET.
- **All clipping tolerances are per-instance; there is no module-global tolerance state left.** `CoordEqTolerance`, `ColinearityTolerance` and `HorizontalAngleTolerance` were previously process-wide mutables (`Geo.coordEqTol`, `Geo.crossColinearityToleranceSqrd`, `Clip.horzAngleTol`) that every `Clipper64` shared and the constructor reset, so configuring one instance leaked into others. They are now instance fields, threaded explicitly into the geometry primitives (`isEqualWithin`/`isNotEqualWithin`, `crossIsZero`/`crossProductSign`/`isColinear`/`pointInPolygon`, `isHorizontalCoords`/`getDx`/`isHorizontal`). Two `Clipper64` instances can now be configured with different tolerances without interfering, and constructing a new instance no longer disturbs existing ones. `MergeVertexTolerance` was already per-instance; the near-top guard constants are now exposed as `NearTopYToleranceFactor` / `NearTopYToleranceCap`. Two stale doc defaults were corrected along the way: `ColinearityTolerance` default is `1e-3` (not `1e-9`) and `CoordEqTolerance` default is `1e-5`.
- README files now describe the current float-preserving public API, helper functions, open-path rules, and test gates.
- Input coordinate snapping moved off `Clipper64` into a standalone, opt-in `Snap` module that mutates `Paths64` in place (`Snap.xAndY` for multiple path collections, `Snap.xAndYSingle` for one collection, with `Snap.DefaultTolerance` = `1e-8`). `Clipper64` no longer snaps; the `Clipper64.SnapXandY` / `SnapXandYTolerance` members and the pre/post-snap callbacks are removed. The `Klipper.*` wrappers do not snap either - call `Snap.xAndY` yourself if you want the pre-pass.
- **Failures raise exceptions.** `Clipper64.Execute` / `ExecutePolyTree` now raise `InvalidOperationException` when the sweep fails (previously the internal `succeeded = false` flag was silently ignored and partial output was returned). Adding an empty path (0 points) via `AddPaths` now raises `ArgumentException` (previously an index crash on .NET, and silent NaN corruption under Fable). `KlipperZ.booleanOpPolyTree` now raises on a null subject like its `Klipper` counterpart (it previously returned an empty tree).
- **Wrappers no longer mutate their inputs.** `Klipper.unionSelfChecked` / `KlipperZ.unionSelfChecked` and `Paths64.ensurePositiveOrientations` / `ensureNegativeOrientations` now build a new list (reusing already-correctly-oriented `Path64` instances) instead of replacing entries of the caller's list in place.
- The internal `evalAtTruncate` / `evalAtRound` pair (identical since coordinates stopped being rounded) collapsed into a single `Clip.evalAt`.

### Fixed
- The open-path-enabled flag is no longer a process-global (`Clip.state.openPathsEnabled`): it is now a `Clipper64` instance field passed explicitly into the `Clip` open-path predicates, so two `Clipper64` instances (e.g. an open-path clip interleaved or concurrent with a closed-path clip) no longer corrupt each other's open-edge classification.
- The Z callback is no longer invoked twice for the same output point when an open edge that is already 'hot' crosses a closed edge (`intersectOpenEdges` had a duplicated `setZ` call).
- `convertHorzSegsToJoins` restores upstream Clipper2's early loop exit on the sorted horizontal-segment list (the port had turned the `break` into a `continue` - same results, quadratic scanning).
- Doc corrections: `MergeVertexTolerance` default is `1e-5` (the `CoordEqTolerance` default), not `1e-6`; `Snap` docs now state that only coordinates in near-axis-aligned segment runs are snapped; various typos.
- A horizontal edge whose bound continues into a *near*-horizontal segment (within
`HorizontalAngleTolerance`, but not exactly flat) no longer leaves that continuation orphaned in
the active edge list. `doHorizontal`'s loop exits on an exact next-vertex-Y comparison while
`updateEdgeIntoAEL` classifies the continuation with the tolerance test, so the segment got
neither a scanline for its top nor a spot in the horizontal queue; its `curX` then evaluated via
`dx = ±infinity` to `±infinity` at the next scanbeam and corrupted the sweep (e.g. a simple
7-point union returned 4 contours with a missing region). The exit path now re-checks
`isHorizontal` and re-queues the edge, mirroring `doTopOfScanbeam`.
- A horizontal bound ending at a local maximum whose maxima-pair edge has not reached the shared
near-flat run yet no longer hangs the sweep in an endless scanbeam ping-pong (with unbounded
memory growth). Clipper2 assumes the pair is already in the AEL - with integer coordinates both
bounds reach a flat run at the same scanline - but with unrounded coordinates the opposite bound
can arrive at a slightly different exact Y, i.e. a later scanbeam. `doHorizontal` then walked the
edge *past* the maximum and back up the opposite bound, re-inserting an already-swept scanline on
every beam (e.g. a single 6-point rectangle from `Test/Rhino/polysXY.json` whose bottom edge
carries ~1e-6 Y-noise looped forever). `doHorizontal` now detects the absent pair up front, keeps
the pass-over range checks active, and parks the edge in the AEL at its top - where the opposite
bound claims it as its maxima pair a few beams later - mirroring `doMaxima`'s null-pair handling;
`Eng.topX` returns `topX` for a parked horizontal-classified edge instead of evaluating
`botX + ±infinity * dy`.
- Horizontal-edge detection is now a tolerance test (`HorizontalAngleTolerance`) instead of exact
`topY = botY`, so a shared near-horizontal edge left a hair off exact by unrounded input (e.g. a
top edge at `37` vs `37.00000000000001`) no longer lands its ends on distinct scanlines and seals
an open notch into a phantom hole - touching polygons union into a single contour without the
`Snap` pre-pass. The default `HorizontalAngleTolerance` was raised from `1e-7` to `1e-6` so that
a near-horizontal bridge edge with slope ratio ~`5e-7` (e.g. a `2`-wide top edge offset by `1e-6`)
is absorbed as horizontal; the `Union-Touching` bridge sweep is now all-success without per-call
tuning, and the JS regression suite is unaffected.
- `cleanColinear` now drops a vertex coincident with a neighbour (within `CoordEqTolerance`) independently
of the colinearity angle test, removing a leftover near-zero-length edge and the horizontal U-turn
spike it propped up in half-snapped touching-polygon unions.
- `Clipper64.AddPaths` now rejects open clip paths explicitly; only subject paths may be open.
- `Clipper64.ExecutePolyTree` now matches `Execute` by returning `null` for the open-path result when no open subjects were added.
- `PolyTree64.ToString()` now includes leaf contours instead of only listing nodes that contain children.
- `Path64` / `Paths64` Z-enabling helpers now reject already-Z-enabled paths and mismatched path/Z collection counts with explicit exceptions.
- Fable `Snap` sorting now preserves sub-unit coordinate differences instead of truncating comparator results to `0`, preventing unrelated nearby scanlines from being averaged together.

## [2.0.1153] - 2026-05-10
### Changed
- MAJOR CHANGE: no more rounding to integers, no more scaling needed, API adapted


## [2.0.1152] - 2026-05-10
### Changed
- MAJOR API refactor  and renaming
### Added
- Test and benchmarks


## [2.0.1151] - 2026-05-01
### Changed
- First release of Port of Clipper2 , ported and adapted from clipper2-ts version 2.0.1-15 to F#

[3.1.0]: https://github.com/goswinr/Klip/compare/3.0.0...3.1.0
[3.0.0]: https://github.com/goswinr/Klip/compare/2.0.1153...3.0.0
[2.0.1153]: https://github.com/goswinr/Klip/compare/2.0.1152...2.0.1153
[2.0.1152]: https://github.com/goswinr/Klip/compare/2.0.1151...2.0.1152
[2.0.1151]: https://github.com/goswinr/Klip/releases/tag/2.0.1151
