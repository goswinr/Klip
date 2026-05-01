
![Logo](https://raw.githubusercontent.com/goswinr/Klip/main/Docs/logo128.png)

# Klip


[![Klip on nuget.org](https://img.shields.io/nuget/v/Klip)](https://www.nuget.org/packages/Klip/)
[![Build Status](https://github.com/goswinr/Klip/actions/workflows/build.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/build.yml)
[![Test Status](https://github.com/goswinr/Klip/actions/workflows/test.yml/badge.svg)](https://github.com/goswinr/Klip/actions/workflows/test.yml)
[![license](https://img.shields.io/github/license/goswinr/Klip)](LICENSE.md)
![code size](https://img.shields.io/github/languages/code-size/goswinr/Klip.svg)

Fast and robust polygon clipping.

A partial F# port of [Clipper2](https://github.com/AngusJ/Clipper2)
The port is derived from the [clipper2-ts](https://github.com/countertype/clipper2-ts)
TypeScript port rather than the original C#.

All tests pass. And its even faster. see [Test README](Test/README.md).

## Why another port?

In short, to use it in F# and be able to compile to JavaScript via [Fable](https://fable.io/)
So that this code - and a lot of other code using it - runs on .NET as well as in the browser.

## Scope

This is a **partial** port - only what's needed for polygon boolean operations
on a single coordinate type:

- Polygon boolean ops (intersection, union, difference, XOR) and PolyTree output
- 64-bit integer coordinates only, but stored as `float` (64-bit) so the output is
  Fable-friendly (no `bigint` in hot paths, no boxed records)
- No offsetting / inflation, line clipping, rect clipping, Minkowski sums,
  triangulation, or arbitrary-precision decimal paths

The public surface is in [`Src/Klip.fs`](Src/Klip.fs) and includes the following key types and operations:

### Core Types

- `Path64`: Contains a sequence of vertices defining a single contour. X, Y, and Z ordinates are stored in parallel float buffers.
- `Paths64`: A list of `Path64` contours, representing multiple paths (e.g. an outer polygon and its holes).
- `PolyTree64`: A specialized data structure used for returning the results of clipping operations. Unlike `Paths64`, which is a flat list, `PolyTree64` preserves the parent-child relationships between contours (such as a hole being a child of an outer contour).
- `ZCallback64`: A callback function that allows you to specify custom logic for calculating the Z-coordinate when two edges intersect and a new vertex is created.

### Fill Rules

Fill rules define which regions of a complex polygon are considered "filled":
- `EvenOdd`: A point is inside if a ray from it crosses an odd number of edges.
- `NonZero`: A point is inside if the winding number is non-zero (default and most common).
- `Positive` / `Negative`: Restricts filling by winding direction.

### Boolean Operations

- `intersect(subject, clip, fillRule)`: Returns the intersection of the subject and clip paths.
- `union(subject, clip, fillRule)`: Returns the union of subject and clip paths.
- `unionSelf(subject, fillRule)`: Resolves self-intersections within a single subject path.
- `difference(subject, clip, fillRule)`: Returns the regions of the subject that are NOT inside the clip region.
- `xor(subject, clip, fillRule)`: Returns the regions of subject or clip that are not in both.
- `booleanOpWithPolyTree(clipType, subject, clip, polyTree, fillRule)`: Computes the boolean operation and outputs the result into a `PolyTree64` to preserve the topological hierarchy.
- `polyTreeToPaths64(polyTree)`: Flattens a `PolyTree64` back into a `Paths64` list.


## Building

for .NET
```bash
dotnet build
```

for  JS
```bash
cd Test
npm install
npm run buildTS   # F# → TypeScript via Fable, then tsc
npm test          # vitest --run
```

The compiled TypeScript ends up in `Klip/Test/_ts/Src/` and is what the test
suite imports.

## Differences from clipper2-ts

- `Path64` is a class with **parallel** `xs[] / ys[] / zs[]` buffers, not an
  array of `{x, y, z}` objects. This is more cache-friendly under Fable's JS
  output and avoids per-point object allocations.
- Fewer features (see Scope above).


# Performance
Klip when compiled to JavaScript is about 30% faster than clipper2-ts.
Tested on union operations with 100 adjacent polygons.

Klip doesn't have its own Point64 object but just uses the parallel `xs[] / ys[] / zs[]` buffers in `Path64`.
The internal Engine objects have just x, y and z properties instead of Point64 objects, and the scanline engine operates on these directly.
So in total fewer JS objects are allocated.
