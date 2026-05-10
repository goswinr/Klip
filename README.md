
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

This is a **partial** port - only what's needed for polygon boolean operations
on a single coordinate type:

- Polygon boolean ops (intersection, union, difference, XOR) and PolyTree output

- like in `clipper2-ts` 64-bit integer coordinates are represented by `float` (64-bit) so the output is
  Fable-friendly (no JS `bigint`) .

- No scaling of input coordinates, like PathD does in `clipper2-ts` to support floating-point input.
Instead, users can choose to scale their coordinates before passing them to Klip if they need more precision.

- No offsetting / inflation, line clipping, rect clipping, Minkowski sums,
  triangulation, or arbitrary-precision decimal paths

The main convenience API is in [`Src/Klip.fs`](https://github.com/goswinr/Klip/blob/main/Src/Klip.fs), in the `Klip.Clipper` module. It wraps `Clipper64` for the common polygon boolean operations while keeping the lower-level engine available for specialized cases.

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



### Boolean Operations


- `Clipper.intersect clip subject`: Returns the intersection of the subject and clip paths.
- `Clipper.union clip subject`: Returns the union of subject and clip paths.
- `Clipper.unionSelf subject`: Resolves self-intersections within a single subject path.
- `Clipper.difference clip subject`: Returns the regions of the subject that are not inside the clip region.
- `Clipper.xor clip subject`: Returns the regions of subject or clip that are not in both.

Each wrapper also has a `Z` variant that takes a `ZCallback64<'Z>` as the first argument:

- `Clipper.intersectZ zCallback clip subject`
- `Clipper.unionZ zCallback clip subject`
- `Clipper.unionSelfZ zCallback subject`
- `Clipper.differenceZ zCallback clip subject`
- `Clipper.xorZ zCallback clip subject`

Use the general functions when you need a custom `ClipType`, `FillRule`, `ZCallback64`, or `PolyTree64` output:

- `Clipper.booleanOp (clipType, subject, clip, fillRule, zCallback)`: Performs a boolean operation and returns `Paths64<'Z>`.
- `Clipper.booleanOpWithPolyTree (clipType, subject, clip, polyTree, fillRule, zCallback)`: Writes the result into a `PolyTree64<'Z>` so hierarchy is preserved.
- `Clipper.polyTreeToPaths64 polyTree`: Flattens a `PolyTree64<'Z>` back into `Paths64<'Z>`.

```fsharp
open Klip

let subject =
    Paths64.createSingle [ 0.0; 0.0; 10.0; 0.0; 10.0; 10.0; 0.0; 10.0 ]

let clip =
    Paths64.createSingle [ 5.0; 5.0; 15.0; 5.0; 15.0; 15.0; 5.0; 15.0 ]

let union = Clipper.union clip subject
let intersection = Clipper.intersect clip subject

let nonZeroDifference =
    Clipper.booleanOp (ClipType.Difference, subject, clip, FillRule.NonZero, None)
```


## Building

for .NET
```bash
dotnet build
```

for  JS
```bash
cd Test
npm install
npm run build     # F# → JavaScript via Fable, then vite build
npm run buildts   # F# → TypeScript via Fable, then tsc and vite build
npm test          # vitest --run
```

The compiled TypeScript ends up in `_dist/Klip.mjs` and is what the test
suite imports.

## Performance

On .NET ist about the same as CLipper2 C#, but with more allocations.
In JavaScript it's about 15% faster than `clipper2-ts`, but 40% slower than `clipper2-wasm`.

See [`Test/bench/README.md`](https://github.com/goswinr/Klip/blob/main/Test/bench/README.md)
and [`Test/README.md`](https://github.com/goswinr/Klip/blob/main/Test/README.md).
