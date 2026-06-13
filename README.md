
![Logo](https://raw.githubusercontent.com/goswinr/Klip/main/Doc/logo128.png)

# Klip


[![Klip on nuget.org](https://img.shields.io/nuget/v/Klip)](https://www.nuget.org/packages/Klip/)
[![Build Status](https://github.com/goswinr/Klip/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/build.yml)
[![Test Status](https://github.com/goswinr/Klip/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/test.yml)
[![license](https://img.shields.io/github/license/goswinr/Klip)](LICENSE.md)
![code size](https://img.shields.io/github/languages/code-size/goswinr/Klip.svg)

A F# library for fast and robust polygon clipping.

Klip is a partial port of [Clipper2](https://github.com/AngusJohnson/Clipper2) to F#.
Only the general polygon boolean operations (intersection, union, difference, XOR) are ported. Offsetting and rectangle-only clipping is not included.
Nor is triangulation.

It uses `float` numbers throughout, not `int64`.

All original and many new tests pass.

It runs on .NET and JavaScript via [Fable](https://fable.io/)
In order to make a port suitable for JS runtimes it is in many parts derived from the TS port of Clipper2 [clipper2-ts](https://github.com/countertype/clipper2-ts)

## Why another port?

### 1 - Fable is amazing
My geometry related F# code can run inside Rhino or Revit but just as well in a browser app.
And I want to use Clipper2 there too.


### 2 - Precision

A second motivation is precision: this port maintains the full position of the input points. The original Clipper2 (and the clipper2-ts port) snaps every coordinate onto an integer grid before clipping. Klip removes that step - the engine computes directly on the unrounded input `float` coordinates, so no conversion to integers happens and the exact input positions are preserved.Also intersection points are computed at full floating-point precision rather than snapped to the grid. See [Coordinate precision (unrounded floats)](#coordinate-precision-unrounded-floats) below for what this entails.

## Scope

### Coordinate precision (unrounded floats)

The clipping engine computes on **unrounded `float` coordinates**. While Clipper2 uses 64-bit integers. (rounded with a user-specified scale factor)

That snapping has been
removed, so vertices created where edges cross are generally **not** integer-valued and keep full
floating-point precision.

What this means in practice:

- Point coincidence and colinearity are no longer tested with exact equality but with small
  tolerances — `abs (a - b) <= Clipper64.CoordEqTolerance` for coordinates, and
  `Clipper64.ColinearityTolerance` for cross-product colinearity. These are sized to absorb
  floating-point noise without fusing genuinely distinct points
- Horizontality is likewise tolerance-based rather than an exact `topY = botY` test: an edge counts
  as horizontal when `abs Δy <= Clipper64.HorizontalAngleTolerance * abs Δx` (a scale-independent
  slope tolerance). This keeps a shared near-horizontal edge that is a hair off exact (e.g. a top
  edge at `37` vs `37.00000000001`) from landing its two ends on distinct scanlines and sealing
  an open notch into a phantom hole. Set it to `0` to restore the exact behaviour.
- Adjacent-edge join checks also avoid fixed integer-grid windows: the near-top guard scales with
  the local edge height and is capped at the old integer-grid limit, and the perpendicular
  join-distance tolerance defaults to the `CoordEqTolerance` value. You can tune that distance on `Clipper64`
  via `MergeVertexTolerance` when your data has unusually noisy or unusually tiny touching edges.
- There is a `Snap` module for preprocessing groups of paths to snap almost aligned x or y coordinates onto their average x or y value. (see below).


- Contours that share a seam are merged, not left separate: horizontal seams join via a dedicated
  pass when their X-ranges overlap (float noise does not defeat this — a real seam's overlap is far
  larger than the noise), and sloped or near-vertical seams join via the adjacent-edge checks gated
  by `MergeVertexTolerance`. If seam-sharing pieces still come out separate, the tolerances are too
  small for your coordinate magnitude — scale them up (see the tolerance notes below) rather than
  rescaling your input. Contours that touch at a single *point* (e.g. the two lobes of an XOR)
  remain separate contours, as in Clipper2.
- You do not need to scale coordinates before clipping to preserve fractional precision. Use your
  source units directly unless your own application deliberately wants a different coordinate unit.
- If you need integer output, **round the solution coordinates yourself after clipping** (e.g. with
  `System.Math.Round`).

### Core Types

Original Documentation: https://www.angusj.com/clipper2

All types are still named like the original C# version, where the `..64` suffix indicated 64-bit integers. In Klip the XY coordinates are stored as `float`, and there is no separate `..D` suffix API because the regular path types already accept and preserve floating-point coordinates.

### New Generic `'Z` Metadata
`'Z` is the generic type parameter for user-defined metadata that can be attached to vertices. It is optional and defaults to `unit` if not used.
In the original Clipper2 the optional Z value is always a `int64` but in Klip it can be any type.


- `Path64<'Z>`: A single contour. X and Y coordinates are stored in a flat interleaved `ResizeArray<float>` as `x0, y0, x1, y1, ...`.
- `Paths64<'Z>`: A `ResizeArray<Path64<'Z>>`, representing multiple contours such as an outer polygon and its holes.
- `PolyTree64<'Z>`: A tree output structure that preserves parent-child contour relationships, such as holes inside outer contours.
- `ZCallback64<'Z>`: A callback for assigning user-defined `'Z` metadata to new vertices created at edge intersections. `'Z` values are metadata, not 3D coordinates.

If you do not use `'Z` metadata, use the default no-Z helpers such as `Path64.createFrom` and `Paths64.createSingle`; these create `Path64<unit>` and `Paths64<unit>` values. The `'Z`-aware helpers and clipping wrappers live in the parallel `Path64`/`Paths64` `...Z` functions and the `KlipperZ` module.

### Path Helpers

The `Path64` and `Paths64` modules provide construction and utility helpers for flat interleaved coordinates:

- `Path64.createFrom`, `Path64.createFromSeq`, `Paths64.createFrom`, and `Paths64.createFromSeq` copy coordinate data into new buffers.
- `Path64.createDirectly` and `Paths64.createDirectly` reuse the supplied `ResizeArray` buffers directly. Coordinates are still floats and are not rounded.
- `Path64.createFromXYMembers` / `createFromxyMembers` and their `Paths64` counterparts accept objects with `X`/`Y` or `x`/`y` members.
- `Path64.enableZ`, `Path64.enableZWith`, `Paths64.enableZ`, and `Paths64.enableZWith` attach metadata buffers and reject paths that already have Z values.
- `mapXY`, `iterXY`, `mapZ`, `iterZ`, orientation helpers, and `signedArea` cover common path inspection and transformation tasks.



## Boolean Operations

### Open vs closed paths

Clipper2 does **not** infer open/closed from coordinates — a trailing vertex equal to the
first one is just stripped, it does not change how the path is treated. Instead, each
path is tagged as open or closed when it is added to the engine.

Rules (inherited from Clipper2):
- Subject paths can be considered open or closed.
- Clip paths are always considered closed.
- For `Intersection`, `Difference`, and `Xor`: closed subject paths are ignored when
  computing the open-path solution, and vice versa — open and closed subjects are
  effectively processed independently.
- For `Union`: open subjects are clipped wherever they overlap any closed path
  (whether that closed path is a subject or a clip).

The `Klipper.*` wrappers below always treat input as **closed** polygons. To clip open
paths (polylines / line segments), drop down to `Clipper64` directly and call
`AddOpenSubject` (or `AddPaths(paths, PathType.Subject, isOpen = true)`):

```fsharp
let c = Clipper64<unit>()
c.AddOpenSubject(openLines)   // polylines — endpoints stay endpoints
c.AddSubject(closedPolygons)  // optional, closed
c.AddClip(clipPolygons)       // clip is always closed
// Execute returns a (closedSolution, openSolution) tuple;
// openSolution is null when no open subjects were added.
let closedSolution, openSolution = c.Execute(ClipType.Intersection, FillRule.EvenOdd)
```

Calling `AddPaths` with `PathType.Clip` and `isOpen = true` is invalid; clip paths are always closed.
`ExecutePolyTree` has the same open-output convention as `Execute`: the open-path result is `null` when no open subjects were added.

### Closed-polygon wrappers

- `Klipper.intersect clip subject`: Returns the intersection of the subject and clip paths.
- `Klipper.union clip subject`: Returns the union of subject and clip paths.
- `Klipper.unionSelf subject`: Resolves self-intersections within a single subject path.
- `Klipper.unionSelfChecked subject`: Reorients all subject paths to positive orientation before unioning them.
- `Klipper.difference clip subject`: Returns the regions of the subject that are not inside the clip region.
- `Klipper.xor clip subject`: Returns the regions of subject or clip that are not in both.
- `Klipper.removeSelfIntersectionsPositive subject` and `Klipper.removeSelfIntersectionsNegative subject`: Resolve one self-intersecting path using the matching directional fill rule.

Each wrapper has a counterpart in the `KlipperZ` module that takes an
`option<ZCallback64<'Z>>` as the first argument, for attaching `'Z` metadata:

- `KlipperZ.intersect zCallback clip subject`
- `KlipperZ.union zCallback clip subject`
- `KlipperZ.unionSelf zCallback subject`
- `KlipperZ.unionSelfChecked zCallback subject`
- `KlipperZ.difference zCallback clip subject`
- `KlipperZ.xor zCallback clip subject`
- `KlipperZ.removeSelfIntersectionsPositive zCallback subject`
- `KlipperZ.removeSelfIntersectionsNegative zCallback subject`

Use the general functions when you need a custom `ClipType`, `FillRule`, or `PolyTree64` output:

- `Klipper.booleanOp (clipType, subject, clip, fillRule)`: Performs a boolean operation and returns `Paths64<unit>`.
- `Klipper.booleanOpPolyTree (clipType, subject, clip, fillRule)`: Returns a `PolyTree64<unit>` so the parent-child contour hierarchy is preserved.
- `Klipper.polyTreeToPaths64 polyTree`: Flattens a `PolyTree64<unit>` back into `Paths64<unit>`.

The `KlipperZ` module mirrors these with a trailing `zCallback` argument and the generic `'Z` type:

- `KlipperZ.booleanOp (clipType, subject, clip, fillRule, zCallback)`
- `KlipperZ.booleanOpPolyTree (clipType, subject, clip, fillRule, zCallback)`
- `KlipperZ.polyTreeToPaths64 polyTree`

Like the wrappers, these also treat subjects as closed. For open-subject clipping use
`Clipper64` directly as shown above.

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

### Direct `Clipper64` Options

Use `Clipper64<'Z>` directly when you need open subjects, repeated execution with the same input, or lower-level tuning:

- `PreserveColinear`: controls whether removable colinear vertices are preserved in closed solutions.
- `CoordEqTolerance`: absolute distance below which two coordinates are treated as the same point (default `1e-5`). Per-instance setting. Independent of `MergeVertexTolerance`.
- `MergeVertexTolerance`: maximum perpendicular distance from a candidate join point to a neighbouring edge for an adjacent-edge join (default `1e-6`). This is the main knob for merging **near-vertical / sloped** touching seams (near-horizontal seams have a separate join pass and tolerate larger noise without tuning). A seam whose two sides are off by a gap `g` needs roughly `MergeVertexTolerance > g`.
- `ColinearityTolerance`: dimensionless angle (`sin θ`) tolerance for cross-product colinearity tests (default `1e-3`). Per-instance setting.
- `HorizontalAngleTolerance`: dimensionless slope tolerance for treating an edge as horizontal — horizontal when `abs Δy <= HorizontalAngleTolerance * abs Δx` (default `1e-6`, set `0` for the exact `topY = botY` test). Per-instance setting.
- `NearTopYToleranceFactor` / `NearTopYToleranceCap`: tune the near-top join guard, which suppresses adjacent-edge joins close to an edge's top vertex. The guard window is `min(NearTopYToleranceCap, edgeHeight * NearTopYToleranceFactor)` (defaults `1e-4` and `2.0`).
- `SmallTriangleTolerance`: absolute window below which a 3-point solution ring is culled as a sliver triangle (default `2.0`, the old integer-grid constant). Per-instance setting.
- `SplitAreaTolerance`: absolute area window for the self-intersection split — a ring is discarded below this area and a split-off triangle kept only above half of it (default `2.0`). Per-instance setting; being an area it scales with the *square* of the coordinate magnitude.
- `ReverseSolution`: reverses output orientation.
- `ZCallback`: computes metadata for vertices created at intersections.

The distance tolerances (`CoordEqTolerance`, `MergeVertexTolerance`, `NearTopYToleranceCap`, `SmallTriangleTolerance`, and the area-valued `SplitAreaTolerance`) are absolute and do not auto-scale. The engine does **not** normalize coordinate magnitude — scale the tolerances you provide to your input instead: with `M` the maximum absolute coordinate, multiply the distance defaults by roughly `M` (and `SplitAreaTolerance` by `M²`); `ColinearityTolerance` and `HorizontalAngleTolerance` are angles and are scale-independent.


### Snap preprocessing
Optionally call `Snap.xAndY tolerance pathGroups` or `Snap.xAndYSingle tolerance paths` to snap x and y coordinates that are almost the same to their respective averages across subject and clip simultaneously.
This is an in-place mutation of the input paths.
You must call this on all paths at once so that x and y get aligned in place across the entire input, and so that the same shared coordinate is used for snapping across subject and clip. This is an optional pre-pass
to cluster nearby input X and Y coordinates independently and mutate the paths *before* adding them to `Clipper64`.

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

for  JS
```bash
cd Test
dotnet tool restore
npm install
npm run clean   # clean previous Fable output
npm run build # F# → JavaScript via Fable, then vite build
npm run buildts # F# → TypeScript via Fable, then tsc and vite build
cd..
```

and then to test:

```bash
cd Test
npm run build # dotnet fable + vite build
npm test # vitest --run
cd ..
```

The JavaScript bundle ends up in `Test/_dist/Klip.mjs` and is what the Vitest suite imports. The TypeScript/Fable build emits a separate bundle under `Test/_distTS/Klip.mjs`.

## Performance

On .NET, the local benchmark harness is roughly on par with Clipper2 C#.
In JavaScript, the latest local benchmark run is about the same as `clipper2-ts` and about 80% slower than `clipper2-wasm` on average.

See [`Test/bench/README.md`](https://github.com/goswinr/Klip/blob/main/Test/bench/README.md)
and [`Test/README.md`](https://github.com/goswinr/Klip/blob/main/Test/README.md).
