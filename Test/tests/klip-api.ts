/**
 * Typed re-exports of the small slice of Klip we test.
 *
 * `_dist/Klip.mjs` is the ESM bundle produced by `npm run build` and ships
 * without `.d.ts` files, so we attach types here at the import site.
 *
 * Surface mirrored from `Klip/Test/_tsc/Src/Klip.d.ts`:
 *   - `booleanOp` / `intersect` / `union` / `unionSelf` / `difference` / `xor`
 *   - `booleanOpWithPolyTree`
 *   - `polyTreeToPaths64`
 *
 * Z-callback variants exist in the bundle but are not used by these tests.
 */

import type { KlipPath64, KlipPaths64, KlipPolyTree64 } from './adapter';
import type { ClipType, FillRule } from './test-data-parser';

// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore -- bundle has no accompanying .d.ts
import * as KlipModule from '../_dist/Klip.mjs';

interface KlipBundle {
  Clipper_booleanOp: KlipApi['booleanOp'];
  Clipper_booleanOpWithPolyTree: KlipApi['booleanOpWithPolyTree'];
  Clipper_polyTreeToPaths64: KlipApi['polyTreeToPaths64'];
  Clipper_intersect: KlipApi['intersect'];
  Clipper_union: KlipApi['union'];
  Clipper_unionSelf: KlipApi['unionSelf'];
  Clipper_difference: KlipApi['difference'];
  Clipper_xor: KlipApi['xor'];
}

interface KlipApi {
  booleanOp(
    clipType: ClipType,
    subject: KlipPaths64,
    clip: KlipPaths64 | null,
    fillRule: FillRule,
    zCallback: undefined,
  ): KlipPaths64;

  booleanOpWithPolyTree(
    clipType: ClipType,
    subject: KlipPaths64,
    clip: KlipPaths64 | null,
    polyTree: KlipPolyTree64,
    fillRule: FillRule,
    zCallback: undefined,
  ): void;

  polyTreeToPaths64(polyTree: KlipPolyTree64): KlipPaths64;

  intersect(clip: KlipPaths64, subject: KlipPaths64): KlipPaths64;
  union(clip: KlipPaths64, subject: KlipPaths64): KlipPaths64;
  unionSelf(subject: KlipPaths64): KlipPaths64;
  difference(clip: KlipPaths64, subject: KlipPaths64): KlipPaths64;
  xor(clip: KlipPaths64, subject: KlipPaths64): KlipPaths64;
}

const bundle = KlipModule as unknown as KlipBundle;

export const Klip: KlipApi = {
  booleanOp: bundle.Clipper_booleanOp,
  booleanOpWithPolyTree: bundle.Clipper_booleanOpWithPolyTree,
  polyTreeToPaths64: bundle.Clipper_polyTreeToPaths64,
  intersect: bundle.Clipper_intersect,
  union: bundle.Clipper_union,
  unionSelf: bundle.Clipper_unionSelf,
  difference: bundle.Clipper_difference,
  xor: bundle.Clipper_xor,
};

export type { KlipPath64, KlipPaths64, KlipPolyTree64 };
