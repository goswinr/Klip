
![Logo](https://raw.githubusercontent.com/goswinr/Klip/main/Doc/logo128.png)

# Klip


[![Klip on nuget.org](https://img.shields.io/nuget/v/Klip)](https://www.nuget.org/packages/Klip/)
[![Build Status](https://github.com/goswinr/Klip/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/build.yml)
[![Test Status](https://github.com/goswinr/Klip/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/test.yml)
[![license](https://img.shields.io/github/license/goswinr/Klip)](LICENSE.md)
![code size](https://img.shields.io/github/languages/code-size/goswinr/Klip.svg)

Fast and robust polygon clipping.

A partial F# port of [Clipper2](https://github.com/AngusJohnson/Clipper2).
This port is derived from the [clipper2-ts](https://github.com/countertype/clipper2-ts)
TypeScript port rather than the original C#.

All original tests pass. There are also some new ones. And it's even a bit faster than `clipper2-ts`. See [bench README](https://github.com/goswinr/Klip/blob/main/Test/bench/README.md).

## Why another port?

In short, to use it in F# and be able to compile to JavaScript via [Fable](https://fable.io/)
So that this code - and a lot of other code using it - runs on .NET as well as in the browser.

## Scope

This is a **partial** port - it exposes polygon boolean operations and offsetting
on a single coordinate type:

- Polygon boolean ops (intersection, union, difference, XOR) and PolyTree output
- Polygon offsetting (inflate / deflate / inset / outset) for closed and open paths,
  with `Miter`, `Square`, `Bevel`, and `Round` joins and the usual end-cap types

- like in `clipper2-ts` 64-bit integer coordinates are represented by `float` (64-bit) so the output is
  Fable-friendly (no JS `bigint`) .

- No scaling of input coordinates, like PathD does in `clipper2-ts` to support floating-point input.
Instead, users can choose to scale their coordinates before passing them to Klip if they need more precision.

- No line clipping, rect clipping, Minkowski sums,
  triangulation, or arbitrary-precision decimal paths

The main convenience API is in [`Src/Klip.fs`](https://github.com/goswinr/Klip/blob/main/Src/Klip.fs), in the `Klip.Klipper` module. It wraps `Clipper64` for the common polygon boolean operations while keeping the lower-level engine available for specialized cases.

### Core Types

Original Documentation: https://www.angusj.com/clipper2

All types are still named like the original C# version, where the `..64` suffix indicated 64-bit integers. In Klip the XY coordinates are stored as `float`, and there is no `..D` suffix API that scales floating-point paths to integer paths.

### New Generic `'Z` Metadata
`'Z` is the generic type parameter for user-defined metadata that can be attached to vertices. It is optional and defaults to `unit` if not used.
In the original Clipper2 the optional Z value is always a `int64` but in Klip it can be any type.


- `Path64<'Z>`: A single contour. X and Y coordinates are stored in a flat interleaved `ResizeArray<float>` as `x0, y0, x1, y1, ...`.
- `Paths64<'Z>`: A `ResizeArray<Path64<'Z>>`, representing multiple contours such as an outer polygon and its holes.
- `PolyTree64<'Z>`: A tree output structure that preserves parent-child contour relationships, such as holes inside outer contours.
- `ZCallback64<'Z>`: A callback for assigning user-defined `'Z` metadata to new vertices created at edge intersections. `'Z` values are metadata, not 3D coordinates.

If you do not use `'Z` metadata, use the default no-Z helpers such as `Path64.createFrom` and `Paths64.createSingle`; these create `Path64<unit>` and `Paths64<unit>` values.



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
let openSolution = Paths64<unit>()
let closedSolution = Paths64<unit>()
c.Execute(ClipType.Intersection, FillRule.EvenOdd, closedSolution, openSolution) |> ignore
```

### Closed-polygon wrappers

- `Klipper.intersect clip subject`: Returns the intersection of the subject and clip paths.
- `Klipper.union clip subject`: Returns the union of subject and clip paths.
- `Klipper.unionSelf subject`: Resolves self-intersections within a single subject path.
- `Klipper.difference clip subject`: Returns the regions of the subject that are not inside the clip region.
- `Klipper.xor clip subject`: Returns the regions of subject or clip that are not in both.

Each wrapper also has a `Z` variant that takes a `ZCallback64<'Z>` as the first argument:

- `Klipper.intersectZ zCallback clip subject`
- `Klipper.unionZ zCallback clip subject`
- `Klipper.unionSelfZ zCallback subject`
- `Klipper.differenceZ zCallback clip subject`
- `Klipper.xorZ zCallback clip subject`

Use the general functions when you need a custom `ClipType`, `FillRule`, `ZCallback64`, or `PolyTree64` output:

- `Klipper.booleanOp (clipType, subject, clip, fillRule, zCallback)`: Performs a boolean operation and returns `Paths64<'Z>`.
- `Klipper.booleanOpWithPolyTree (clipType, subject, clip, polyTree, fillRule, zCallback)`: Writes the result into a `PolyTree64<'Z>` so hierarchy is preserved.
- `Klipper.polyTreeToPaths64 polyTree`: Flattens a `PolyTree64<'Z>` back into `Paths64<'Z>`.

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
    Klipper.booleanOp (ClipType.Difference, subject, clip, FillRule.NonZero, None)
```


## Offsetting

Inflating (positive `delta`) and deflating (negative `delta`) of polygons:

- `Klipper.inflate delta paths`: Inflates / deflates closed polygons using round joins and default tolerances.
- `Klipper.inflatePaths (paths, delta, joinType, miterLimit, arcTolerance)`: Same, with explicit join type and tolerances.
- `Klipper.offsetOpenPaths (paths, delta, joinType, endType, miterLimit, arcTolerance)`: Offsets open paths (lines), with the specified end-cap shape (`Butt`, `Square`, `Round`, or `Joined`).

### Open vs closed paths

For offsetting, open / closed is selected by the `EndType` passed alongside the path —
not by whether the first and last coordinates match. The choice also affects the result
shape:

- `EndType.Polygon`: treats the path as a **closed polygon**. The offset is applied on
  one side (outside for positive `delta`, inside for negative) and the result is again
  a closed polygon. This is what `Klipper.inflate` / `Klipper.inflatePaths` use.
- `EndType.Joined`: treats the path as **open** but joins the first vertex to the last,
  so the offset wraps around both sides of the polyline and closes back on itself.
- `EndType.Butt` / `EndType.Square` / `EndType.Round`: treats the path as an **open
  polyline**. The offset runs along both sides of the line and the two ends are capped
  with the named shape (perpendicular cut, squared overhang, or rounded).

`joinType` is one of `JoinType.Miter`, `JoinType.Square`, `JoinType.Bevel`, `JoinType.Round`
and controls how corners *between* segments are constructed in all cases.

Use `ClipperOffset<'Z>` directly for full control over multiple groups (each with its
own `joinType` / `endType` — so you can mix open and closed input in one execution),
`ZCallback`, `DeltaCallback`, `PolyTree64` output, and reused state:

```fsharp
let co = ClipperOffset<unit>(miterLimit = 2.0, arcTolerance = 0.25)
co.AddPaths(closedPolys, JoinType.Round, EndType.Polygon)
co.AddPaths(polylines,   JoinType.Round, EndType.Round)
let solution = Paths64<unit>()
co.Execute(10.0, solution)
```


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
npm install
npm run build     # F# → JavaScript via Fable, then vite build
npm run buildts   # F# → TypeScript via Fable, then tsc and vite build
```

and then to test:
```bash
npm test          # vitest --run
```

The compiled TypeScript ends up in `_dist/Klip.mjs` and is what the test
suite imports.

## Performance

On .NET ist about the same as CLipper2 C#, but with more allocations.
In JavaScript it's about 15% faster than `clipper2-ts`, but 40% slower than `clipper2-wasm`.

See [`Test/bench/README.md`](https://github.com/goswinr/Klip/blob/main/Test/bench/README.md)
and [`Test/README.md`](https://github.com/goswinr/Klip/blob/main/Test/README.md).
