
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

This is a **partial** port - it exposes polygon boolean operations
on a single coordinate type:

- Polygon boolean ops (intersection, union, difference, XOR) and PolyTree output

- like in `clipper2-ts` 64-bit integer coordinates are represented by `float` (64-bit) so the output is
  Fable-friendly (no JS `bigint`) .

- No scaling of input coordinates, like PathD does in `clipper2-ts` to support floating-point input.
Instead, users can choose to scale their coordinates before passing them to Klip if they need more precision.

- No polygon offsetting, line clipping, rect clipping, Minkowski sums,
  triangulation, or arbitrary-precision decimal paths

The main convenience API is in [`Src/Klip.fs`](https://github.com/goswinr/Klip/blob/main/Src/Klip.fs), in the `Klip.Klipper` module. It wraps `Clipper64` for the common polygon boolean operations while keeping the lower-level engine available for specialized cases.

### Coordinate precision (unrounded floats)

The clipping engine computes on **unrounded `float` coordinates**. Earlier versions snapped every
computed intersection point to the nearest integer (the old `Geo.jsRound`); that snapping has been
removed, so vertices created where edges cross are generally **not** integer-valued and keep full
floating-point precision.

What this means in practice:

- Point coincidence and collinearity are no longer tested with exact equality but with small
  tolerances — `abs (a - b) < 1e-6` for coordinates, and a relative tolerance for cross-product
  collinearity. These are sized to absorb floating-point noise without fusing genuinely distinct
  points (real coordinates can be as little as one unit apart).
- Because there is no longer an integer grid, complex inputs can occasionally resolve into a few
  more (or fewer) *touching* contours than an integer-snapped clipper would. Areas stay within the
  usual tolerances, and a follow-up `union` simplifies touching contours if needed.
- If you need integer output, **round the solution coordinates yourself after clipping** (e.g. with
  `System.Math.Round`). Note that, because they delegate to `jsRound` (now the identity function),
  `Floats.round` is currently a no-op and `Floats.scaleUpAndRound` only scales without rounding.

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
