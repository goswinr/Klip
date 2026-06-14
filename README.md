
![Logo](https://raw.githubusercontent.com/goswinr/Klip/main/Doc/logo128.png)

# Klip


[![Klip on nuget.org](https://img.shields.io/nuget/v/Klip)](https://www.nuget.org/packages/Klip/)
[![Build Status](https://github.com/goswinr/Klip/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/build.yml)
[![Test Status](https://github.com/goswinr/Klip/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/test.yml)
[![license](https://img.shields.io/github/license/goswinr/Klip)](LICENSE.md)
![code size](https://img.shields.io/github/languages/code-size/goswinr/Klip.svg)

A F# library for fast and robust polygon clipping.

Klip is a partial port of [Clipper2](https://github.com/AngusJohnson/Clipper2) covering the general
polygon boolean operations — **intersection, union, difference, and XOR**. Offsetting, rectangle-only
clipping, and triangulation are not included.

It runs on .NET and JavaScript via [Fable](https://fable.io/), so the same source serves Rhino, Revit,
and browser apps. To make it suitable for JS runtimes it is in many parts derived from the TypeScript
port [clipper2-ts](https://github.com/countertype/clipper2-ts). All original and many new tests pass.

The key difference from Clipper2: Klip uses `float` coordinates throughout instead of `int64`.
Clipper2 snaps every coordinate onto an integer grid before clipping; Klip removes that step and computes
directly on the unrounded input. Intersection points are kept at full floating-point precision rather than
snapped to the grid, so the exact input positions are preserved. See
[Coordinate precision](#coordinate-precision) for what this entails.

## Coordinate precision

Because the engine computes on unrounded `float` coordinates, point coincidence and colinearity use small
tolerances instead of exact equality. The defaults absorb floating-point noise without fusing genuinely
distinct points; all are per-instance settings on `Clipper64`:

- **Point coincidence** — coordinates are equal when `abs (a - b) <= CoordEqTolerance`.
- **Colinearity** — tested via `ColinearityTolerance` on the cross product.
- **Horizontality** — an edge is horizontal when `abs Δy <= HorizontalAngleTolerance * abs Δx`, rather
  than an exact `topY = botY` test. This keeps a shared near-horizontal edge that is a hair off exact
  (e.g. a top at `37` vs `37.00000000001`) from landing its ends on distinct scanlines and sealing an
  open notch into a phantom hole. Set it to `0` to restore exact behaviour.
- **Adjacent-edge joins** — the near-top guard scales with local edge height (capped at the old
  integer-grid limit), and the perpendicular join distance defaults to `CoordEqTolerance`. Tune it via
  `MergeVertexTolerance` for unusually noisy or tiny touching edges.

Contours that share a seam are merged: horizontal seams join when their X-ranges overlap (a real seam's
overlap far exceeds float noise), and sloped/near-vertical seams join via the adjacent-edge checks gated
by `MergeVertexTolerance`. Contours that touch at a single *point* (e.g. the two lobes of an XOR) remain
separate, as in Clipper2. If seam-sharing pieces come out separate, the tolerances are too small for your
coordinate magnitude — scale them up (see [Tolerances and scaling](#tolerances-and-scaling)) rather than
rescaling your input.

You do not need to scale coordinates before clipping — use your source units directly. If you need integer
output, **round the solution coordinates yourself after clipping** (e.g. with `System.Math.Round`).

The `Snap` module can optionally pre-snap almost-aligned coordinates (see [below](#snap-preprocessing)).

## Types

Original documentation: https://www.angusj.com/clipper2

Types keep their original C# names. The `..64` suffix historically meant 64-bit integers; in Klip the XY
coordinates are `float`, and there is no separate `..D` API because the regular path types already preserve
floating-point coordinates.

- `Path64<'Z>`: a single contour. X and Y are stored in a flat interleaved `ResizeArray<float>` as
  `x0, y0, x1, y1, ...`.
- `Paths64<'Z>`: a `ResizeArray<Path64<'Z>>` — multiple contours, such as an outer polygon and its holes.
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

- `intersect clip subject` — intersection of subject and clip.
- `union clip subject` — union of subject and clip.
- `unionSelf subject` — resolves self-intersections within a single subject.
- `unionSelfChecked subject` — reorients all subjects to positive orientation before unioning.
- `difference clip subject` — regions of subject not inside clip.
- `xor clip subject` — regions in subject or clip but not both.
- `removeSelfIntersectionsPositive subject` / `removeSelfIntersectionsNegative subject` — resolve one
  self-intersecting path using the matching directional fill rule.

For a custom `ClipType`, `FillRule`, or `PolyTree64` output:

- `booleanOp (clipType, subject, clip, fillRule)` — returns `Paths64<unit>`.
- `booleanOpPolyTree (clipType, subject, clip, fillRule)` — returns a `PolyTree64<unit>` preserving the
  parent-child hierarchy.
- `polyTreeToPaths64 polyTree` — flattens a `PolyTree64<unit>` back into `Paths64<unit>`.

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

Open/closed is **not** inferred from coordinates (a trailing vertex equal to the first is just stripped) —
each path is tagged when added to the engine. Rules, inherited from Clipper2:

- Subject paths can be open or closed; clip paths are always closed.
- For `Intersection`, `Difference`, and `Xor`: open and closed subjects are processed independently — closed
  subjects are ignored for the open-path solution, and vice versa.
- For `Union`: open subjects are clipped wherever they overlap any closed path (subject or clip).

The `Klipper.*` and `KlipperZ.*` wrappers always treat input as closed. To clip open paths
(polylines / line segments), use `Clipper64` directly and call `AddOpenSubject`:

```fsharp
let c = Clipper64<unit>()
c.AddOpenSubject(openLines)   // polylines — endpoints stay endpoints
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
- `CoordEqTolerance`: absolute distance below which two coordinates are the same point (default `1e-5`).
- `MergeVertexTolerance`: max perpendicular distance from a candidate join point to a neighbouring edge for
  an adjacent-edge join (default `1e-6`). Main knob for merging near-vertical / sloped touching seams; a
  seam with a gap `g` needs roughly `MergeVertexTolerance > g`. (Near-horizontal seams have a separate join
  pass and need no tuning.)
- `ColinearityTolerance`: dimensionless angle (`sin θ`) tolerance for cross-product colinearity (default `1e-3`).
- `HorizontalAngleTolerance`: dimensionless slope tolerance for treating an edge as horizontal (default
  `1e-6`; set `0` for the exact `topY = botY` test).
- `NearTopYToleranceFactor` / `NearTopYToleranceCap`: tune the near-top join guard window
  `min(NearTopYToleranceCap, edgeHeight * NearTopYToleranceFactor)` (defaults `2.0` and `1e-4`).
- `SmallTriangleTolerance`: absolute window below which a 3-point solution ring is culled as a sliver
  (default `2.0`, the old integer-grid constant).
- `SplitAreaTolerance`: absolute area window for the self-intersection split (default `2.0`); being an area
  it scales with the *square* of the coordinate magnitude.
- `ReverseSolution`: reverses output orientation.
- `ZCallback`: computes metadata for vertices created at intersections.

### Tolerances and scaling

The distance tolerances (`CoordEqTolerance`, `MergeVertexTolerance`, `NearTopYToleranceCap`,
`SmallTriangleTolerance`, and the area-valued `SplitAreaTolerance`) are absolute and do **not** auto-scale —
the engine does not normalize coordinate magnitude. With `M` the maximum absolute coordinate, multiply the
distance defaults by roughly `M` (and `SplitAreaTolerance` by `M²`). The angle tolerances
(`ColinearityTolerance`, `HorizontalAngleTolerance`) are scale-independent.

### Snap preprocessing

Optionally call `Snap.xAndY tolerance pathGroups` or `Snap.xAndYSingle tolerance paths` to snap nearly-equal
x and y coordinates to their respective averages. This is an in-place mutation done *before* adding paths to
`Clipper64`. Call it on all paths at once so the same shared coordinate is used across subject and clip.

## Building

for .NET
```bash
dotnet build
```

test:
```bash
dotnet test Test/FSharp/Tests/Tests1/Tests1.fsproj
dotnet test Test/FSharp/Tests/Tests2/TestsZ.fsproj
```

for JS
```bash
cd Test
dotnet tool restore
npm install
npm run clean   # clean previous Fable output
npm run build   # F# → JavaScript via Fable, then vite build
npm run buildts # F# → TypeScript via Fable, then tsc and vite build
cd ..
```

and then to test:

```bash
cd Test
npm run build # dotnet fable + vite build
npm test      # vitest --run
cd ..
```

The JavaScript bundle ends up in `Test/_dist/Klip.mjs` and is what the Vitest suite imports. The
TypeScript/Fable build emits a separate bundle under `Test/_distTS/Klip.mjs`.

## Performance

On .NET, the local benchmark harness is roughly on par with Clipper2 C#. In JavaScript, the latest local
run is about the same as `clipper2-ts` and about 80% slower than `clipper2-wasm` on average.

See [`Test/bench/README.md`](https://github.com/goswinr/Klip/blob/main/Test/bench/README.md)
and [`Test/README.md`](https://github.com/goswinr/Klip/blob/main/Test/README.md).
