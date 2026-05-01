// Duck-typed bridge from clipper2-ts shapes to Klip's `Path64` parallel-buffer
// shape, plus a re-export of the Klip ops bundled in `_dist/Klip.mjs`.
//
// Klip's `Path64` class is a plain `{ xs, ys, zs }` object after Fable
// compilation, so we don't need the actual class — a literal works. Same for
// `PolyTree64` (`{ children, _parent, polygon }`). This lets us bench against
// the production bundle without exporting internals from `Klip.mjs`.

import type { Path64, Paths64 } from 'clipper2-ts';

// Klip-shaped types (matching the compiled Fable output).
export type LipPath64 = { xs: number[]; ys: number[]; zs: any[] };
export type LipPaths64 = LipPath64[];
export interface LipPolyTree64 { children: any[]; _parent: any; polygon: any }

export function toLipPath(path: Path64): LipPath64 {
  const n = path.length;
  const xs = new Array<number>(n);
  const ys = new Array<number>(n);
  const zs = new Array<any>(n);
  for (let i = 0; i < n; i++) {
    xs[i] = path[i].x;
    ys[i] = path[i].y;
    zs[i] = undefined;
  }
  return { xs, ys, zs };
}

export function toLipPaths(paths: Paths64): LipPaths64 {
  const out: LipPaths64 = new Array(paths.length);
  for (let i = 0; i < paths.length; i++) out[i] = toLipPath(paths[i]);
  return out;
}

export function createLipPolyTree(): LipPolyTree64 {
  return { children: [], _parent: undefined, polygon: undefined };
}

// Re-export the Klip surface from the production bundle. Untyped because
// `_dist/Klip.mjs` ships without `.d.ts` — the bench code casts where needed.
// @ts-expect-error: bundle has no types
export {
  booleanOp,
  booleanOpWithPolyTree,
  intersect,
  union,
  unionSelf,
  difference,
  xor,
  polyTreeToPaths64
} from '../_dist/Klip.mjs';
