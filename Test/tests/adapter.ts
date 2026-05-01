/**
 * Format adapter between clipper2-ts-style paths (`Point64[]`) and Klip's
 * `Path64` class with parallel `xs[]/ys[]/zs[]` buffers.
 *
 * Tests parse data into `Point64[]` (the natural shape for the test data files),
 * then call this adapter to materialize Klip-shaped inputs and to extract results
 * back to `Point64[]` for area / count comparisons.
 */

import {
  Path64 as LipPath64,
  Path64_$ctor,
  Path64__Add_Z6A810145,
  Path64__get_Xs,
  Path64__get_Ys,
  Path64__get_Count,
  Geo_area
} from '../_ts/Src/Core.ts';

import type { Point64, Path64 as TsPath64, Paths64 as TsPaths64 } from './test-data-parser';

/** Convert a clipper2-ts-style path to a Klip Path64. */
export function toLipPath(path: TsPath64): LipPath64 {
  const out = Path64_$ctor();
  for (const pt of path) {
    Path64__Add_Z6A810145(out, pt.x, pt.y, undefined);
  }
  return out;
}

/** Convert clipper2-ts-style paths to Klip Path64[]. */
export function toLipPaths(paths: TsPaths64): LipPath64[] {
  const out: LipPath64[] = [];
  for (const p of paths) out.push(toLipPath(p));
  return out;
}

/** Convert a Klip Path64 back to a clipper2-ts-style path (`Point64[]`). */
export function fromLipPath(path: LipPath64): TsPath64 {
  const xs = Path64__get_Xs(path);
  const ys = Path64__get_Ys(path);
  const cnt = Path64__get_Count(path);
  const out: Point64[] = [];
  for (let i = 0; i < cnt; i++) out.push({ x: xs[i], y: ys[i] });
  return out;
}

/** Convert Klip Path64[] back to clipper2-ts-style paths. */
export function fromLipPaths(paths: LipPath64[]): TsPaths64 {
  const out: TsPaths64 = [];
  for (const p of paths) out.push(fromLipPath(p));
  return out;
}

/** Signed area of a single Klip Path64 (matches `Clipper.area`). */
export function area(path: LipPath64): number {
  return Geo_area(path);
}

/** Sum of signed areas of a Klip Paths64 (matches `Clipper.areaPaths`). */
export function areaPaths(paths: LipPath64[]): number {
  let a = 0;
  for (const p of paths) a += Geo_area(p);
  return a;
}
