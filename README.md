![Logo](https://raw.githubusercontent.com/goswinr/Klip/main/Doc/logo128.png)

# Klip

[![Klip on nuget.org](https://img.shields.io/nuget/v/Klip)](https://www.nuget.org/packages/Klip/)
[![Build Status](https://github.com/goswinr/Klip/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/build.yml)
[![Test Status](https://github.com/goswinr/Klip/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/test.yml)
[![license](https://img.shields.io/github/license/goswinr/Klip)](LICENSE.md)
![code size](https://img.shields.io/github/languages/code-size/goswinr/Klip.svg)

An F# library for fast and robust polygon clipping.

Klip is a partial port of [Clipper2](https://github.com/AngusJohnson/Clipper2) covering the general
polygon boolean operations - **intersection, union, difference, and XOR**. Offsetting, rectangle-only
clipping, and triangulation are not included.

It runs on .NET and JavaScript via [Fable](https://fable.io/), so the same source serves Rhino, Revit,
and browser apps. To make it suitable for JS runtimes it is in many parts derived from the TypeScript
port [clipper2-ts](https://github.com/countertype/clipper2-ts). All original tests pass, along with many new
tests for unions of almost-aligned polygons.

The key difference from Clipper2: Klip uses `float` coordinates throughout instead of `int64`.
Clipper2 snaps every coordinate onto an integer grid before clipping; Klip removes that step and computes
directly on the unrounded input. Intersection points are kept at full floating-point precision rather than
snapped to the grid, so the exact input positions are preserved.

## Coordinate precision

Because the engine computes on unrounded `float` coordinates, point coincidence, colinearity, and
horizontality use small per-instance tolerances instead of exact equality. The defaults absorb
floating-point noise without fusing genuinely distinct points:

- **Point coincidence** - two coordinates are the same point when they differ by less than a small
  absolute distance.
- **Colinearity** - three points are colinear when the turn angle between their edges falls below a
  small scale-free angle tolerance.
- **Horizontality** - an edge is horizontal when its slope falls below a small scale-free angle
  tolerance, rather than an exact `topY = botY` test. This keeps a shared near-horizontal edge that is a
  hair off exact (e.g. a top at `37` vs `37.000001`) from landing its ends on distinct scanlines
  and sealing an open notch into a phantom hole.
- **Adjacent-edge joins** - the near-top guard scales with local edge height, and the perpendicular
  join distance defaults to the point-coincidence tolerance.

Contours that share a seam are merged: horizontal seams join when their X-ranges overlap (a real seam's
overlap far exceeds float noise), and sloped/near-vertical seams join via the tolerance-gated
adjacent-edge checks. Contours that touch at a single *point* (e.g. the two lobes of an XOR) remain
separate, as in Clipper2.

You do not need to scale coordinates before clipping - use your source units directly. If results are off
for your coordinate magnitude (e.g. seam-sharing pieces come out separate, or slivers survive), the
tolerances don't fit your inputs: set them all from one absolute tolerance with the `Tolerance`
property (see [Tolerances and scaling](#tolerances-and-scaling)) rather than rescaling your input.

The `Snap` module can optionally pre-snap almost-aligned coordinates (see [below](#snap-preprocessing)).

## Types

Original documentation: https://www.angusj.com/clipper2

Types keep their original C# names. The `..64` suffix historically meant 64-bit integers; in Klip the XY
coordinates are `float`, and there is no separate `..D` API because the regular path types already preserve
floating-point coordinates.

- `Path64<'Z>`: a single contour. X and Y are stored in a flat interleaved `ResizeArray<float>` as
  `x0, y0, x1, y1, ...`.
- `Paths64<'Z>`: a `ResizeArray<Path64<'Z>>` - multiple contours, such as an outer polygon and its holes.
- `PolyTree64<'Z>`: a tree output that preserves parent-child contour relationships (holes inside outers).
- `ZCallback64<'Z>`: a callback assigning user-defined `'Z` metadata to vertices created at intersections.

### Generic `'Z` metadata

`'Z` is an optional generic type parameter for user-defined metadata attached to vertices, defaulting to
`unit`. (In the original Clipper2 the optional Z value is always `int64`.) `'Z` values are metadata, **not**
a 3rd coordinate.

If you do not use `'Z`, use the no-Z helpers such as `Path64.createFrom` and `Paths64.createSingle`, which
produce `Path64<unit>` / `Paths64<unit>` values. The `'Z`-aware helpers live in the parallel `...Z`
functions and the `KlipperZ` module.

### Path helpers

The `Path64` and `Paths64` modules provide construction and utility helpers:

- `createFrom`, `createFromSeq` (on both modules) copy coordinate data into new buffers.
- `createDirectly` reuses the supplied `ResizeArray` buffers directly (coordinates are not rounded).
- `createFromXYMembers` / `createFromxyMembers` accept objects with `X`/`Y` or `x`/`y` members.
- `enableZ` / `enableZWith` attach metadata buffers and reject paths that already have Z values.
- `mapXY`, `iterXY`, `mapZ`, `iterZ`, orientation helpers, and `signedArea` cover common inspection and
  transformation tasks.

## Boolean operations

### Closed-polygon wrappers

The `Klipper.*` wrappers always treat input as **closed** polygons:

- `intersect clip subject` - intersection of subject and clip.
- `union clip subject` - union of subject and clip.
- `unionSelf subject` - resolves self-intersections within a single subject.
- `unionSelfChecked subject` - reorients all subjects to positive orientation before unioning.
- `difference clip subject` - regions of subject not inside clip.
- `xor clip subject` - regions in subject or clip but not both.
- `removeSelfIntersectionsPositive subject` / `removeSelfIntersectionsNegative subject` - resolve one
  self-intersecting path using the matching directional fill rule.

For a custom `ClipType`, `FillRule`, or `PolyTree64` output:

- `booleanOp (clipType, subject, clip, fillRule)` - returns `Paths64<unit>`.
- `booleanOpPolyTree (clipType, subject, clip, fillRule)` - returns a `PolyTree64<unit>` preserving the
  parent-child hierarchy.
- `polyTreeToPaths64 polyTree` - flattens a `PolyTree64<unit>` back into `Paths64<unit>`.

Each function has a counterpart in the `KlipperZ` module that takes an `option<ZCallback64<'Z>>` (first
argument for the wrappers, trailing `zCallback` argument for `booleanOp` / `booleanOpPolyTree`) to attach
`'Z` metadata.

```fsharp
open Klip

let subject =
    Paths64.createSingle [ 0.0; 0.0; 10.0; 0.0; 10.0; 10.0; 0.0; 10.0 ]

let clip =
    Paths64.createSingle [ 5.0; 5.0; 15.0; 5.0; 15.0; 15.0; 5.0; 15.0 ]

let union = Klipper.union clip subject
let intersection = Klipper.intersect clip subject

let nonZeroDifference =
    Klipper.booleanOp (ClipType.Difference, subject, clip, FillRule.NonZero)
```

### Open vs closed paths

Open/closed is **not** inferred from coordinates (a trailing vertex equal to the first is just stripped) -
each path is tagged when added to the engine. Rules, inherited from Clipper2:

- Subject paths can be open or closed; clip paths are always closed.
- For `Intersection`, `Difference`, and `Xor`: open and closed subjects are processed independently - closed
  subjects are ignored for the open-path solution, and vice versa.
- For `Union`: open subjects are clipped wherever they overlap any closed path (subject or clip).

The `Klipper.*` and `KlipperZ.*` wrappers always treat input as closed. To clip open paths
(polylines / line segments), use `Clipper64` directly and call `AddOpenSubject`:

```fsharp
let c = Clipper64<unit>()
c.AddOpenSubject(openLines)   // polylines - endpoints stay endpoints
c.AddSubject(closedPolygons)  // optional, closed
c.AddClip(clipPolygons)       // clip is always closed
// Execute returns a (closedSolution, openSolution) tuple;
// openSolution is null when no open subjects were added.
let closedSolution, openSolution = c.Execute(ClipType.Intersection, FillRule.EvenOdd)
```

Calling `AddPaths` with `PathType.Clip` and `isOpen = true` is invalid. `ExecutePolyTree` follows the same
open-output convention as `Execute`.

### Direct `Clipper64` options

Use `Clipper64<'Z>` directly for open subjects, repeated execution with the same input, or lower-level
tuning:

- `PreserveColinear`: keep removable colinear vertices in closed solutions.
- Constructor `tolerance`: initializes all five scale-dependent tolerances from one absolute distance.
  `Tolerance` reports this fixed value; see [Tolerances and scaling](#tolerances-and-scaling).
- Optional constructor `angleTolerance`: initializes the angle in degrees. The `AngleTolerance`
  property remains adjustable after construction.
- `ReverseSolution`: reverses output orientation.
- `ZCallback`: computes metadata for vertices created at intersections.

The individual execution tolerance properties (adjacent-edge joins, colinearity, horizontality,
the near-top join guard, and the sliver culls) remain functional as expert overrides but are marked
`[<Obsolete>]`. `CoordEqTolerance` is a read-only alias for `Tolerance`. Use the constructor's
`tolerance` argument and the mutable `AngleTolerance` property for ordinary tuning; editor visibility
of obsolete members depends on the tooling.
Each is documented in detail on the member itself in `Src/Engine.fs`.

### Tolerances and scaling

The distance tolerances are absolute and do **not** auto-scale - the engine does not normalize coordinate
magnitude. Initialize them with `Clipper64<unit>(tolerance = t)` - the distance below which
points are considered identical and lines touching: the four distance tolerances become `t`, and the area-valued split
tolerance becomes `t²` (valid range `0.0 .. 1e12`; `0` makes the comparisons exact). The value is used
as-is, not as a multiplier of the defaults. `Clipper64<unit>()` uses `1e-5`: all four distance
thresholds are `1e-5`, and the split-area threshold is `1e-10`.
Unit and subunit triangles do not need a custom tolerance to bypass integer-grid culling defaults.

Coordinate tolerance is **constructor-only**: input ingestion discards near-duplicate vertices.
Both `Tolerance` and `CoordEqTolerance` are read-only. `ClearAll()` removes geometry while retaining
the constructor tolerance and execution settings. Create a new instance and re-add the original
paths to use a different coordinate tolerance. Repeated execution with the same inputs is supported.
Changing the distance on an existing instance could not recover vertices already discarded during
ingestion. `AngleTolerance` is applied to fresh execution state and output geometry, leaving the
stored input vertices intact, so it can be changed between executions without re-adding the paths.

```fsharp
let c = Clipper64<unit>(tolerance = 1e-6, angleTolerance = 0.05)
c.AddSubject(subject)
let closed, opened = c.Execute(ClipType.Union, FillRule.NonZero)
```

Both constructor arguments may be omitted. `Clipper64<unit>(angleTolerance = 0.05)` uses
the default coordinate tolerance of `1e-5`. Omitting the angle retains the default of about
`0.057295789` degrees (`sin(angle) = 1e-3`). The constructor angle has the same valid range
and effect as setting `AngleTolerance`; that property remains mutable, including after adding paths.

Migrate `c.Tolerance <- t` or `c.CoordEqTolerance <- t` to the constructor argument.
It initializes all five thresholds; apply any separate execution-only expert overrides afterwards.

Near equality is not transitive. For closed input paths with adjacent near duplicates,
the engine chooses representatives in the lexicographically smallest cyclic traversal
across both directions, then restores the input winding. Rotating the start vertex or
reversing the path therefore keeps the same coordinates; each retained vertex keeps its
own Z value. Open paths retain their supplied direction and use sequential deduplication.

Reassigning `c.AngleTolerance <- c.AngleTolerance` preserves expert horizontal overrides.
Assigning a different angle updates both angular thresholds together. The angle range is `0 .. asin(0.1)`
degrees (about 5.739), and the corresponding expert sine range is `0 .. 0.1`, including
exact mode at zero. Larger sine values are rejected because they cannot round-trip through
the supported angle range.

`AngleTolerance` controls colinear cleanup and adjacent-edge joins (and derives the tighter horizontal
angle threshold). Cleanup also requires the removed vertex's perpendicular deviation to fit the
absolute coordinate tolerance, so a small angle alone cannot erase a large thin polygon.
It does not flatten orientation signs used for edge ordering or proper segment
crossings. Point-on-boundary checks use the absolute coordinate tolerance, followed by exact
ray-crossing comparisons for containment.

Scale distances with the input coordinates and area thresholds with the square of that scale;
angle tolerances are dimensionless. Floating-point rounding and representable range still limit
scale invariance. Orientation signs use an exact fallback for the supplied finite double values,
and areas use local origins with compensated summation. Constructed intersection coordinates
remain doubles, and cannot recover detail already lost when the input coordinates were rounded.
All input XY coordinates must be finite. A failed `AddPaths` validation adds none of its paths.

### Snap preprocessing

Optionally call `Snap.xAndY tolerance pathGroups` or `Snap.xAndYSingle tolerance paths` to snap nearly-equal
x and y coordinates to their respective averages. This is an in-place mutation done *before* adding paths to
`Clipper64`. Call it on all paths at once so the same shared coordinate is used across subject and clip.

Snapping treats paths as closed. Each vertex qualifies on an axis if either neighbour is within
tolerance on that axis, and contributes once to the average. Sorted clusters have a total width
at most the tolerance; chains of close neighbours do not merge into an unbounded cluster.
This is one pass over the original geometry: repeated snapping can form new clusters, so it is
not an idempotent normalization operation. Identical coordinates remain bit-exact, even at zero
tolerance. Invalid tolerances or nonfinite coordinates are rejected before any buffer is changed.
`Snap.DefaultTolerance` is `1e-5`; pass it explicitly when desired.

## Building

For .NET:

```bash
dotnet build
dotnet test Test/FSharp/Tests/Tests1/Tests1.fsproj
dotnet test Test/FSharp/Tests/Tests2/TestsZ.fsproj
```

For JavaScript:

```bash
cd Test/TypeScript
dotnet tool restore
npm install
npm run clean   # clean previous Fable output
npm run build   # F# → JavaScript via Fable, then vite build
npm test        # vitest --run, against the compiled bundle
npm run buildts # optional: F# → TypeScript via Fable, then tsc and vite build
cd ../..
```

The JavaScript bundle ends up in `Test/TypeScript/_dist/Klip.mjs` and is what the Vitest suite imports - rebuild
before testing after any F# source change. The TypeScript/Fable build emits a separate bundle under
`Test/TypeScript/_distTS/Klip.mjs`.

## Performance

On .NET, the local benchmark harness is roughly on par with Clipper2 C#. In JavaScript, the latest local
run is about the same as `clipper2-ts` and about 80% slower than `clipper2-wasm` on average.

See [`Test/TypeScript/bench/README.md`](https://github.com/goswinr/Klip/blob/main/Test/TypeScript/bench/README.md)
and [`Test/README.md`](https://github.com/goswinr/Klip/blob/main/Test/README.md).
