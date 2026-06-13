/**
 * Typed re-exports of the small slice of Klip we test.
 *
 * `_dist/Klip.mjs` is the ESM bundle produced by `npm run build` and ships
 * without `.d.ts` files, so we attach types here at the import site.
 *
 * Surface mirrored from `Klip/Test/_tsc/Src/Klip.d.ts`:
 *   - `booleanOp` / `intersect` / `union` / `unionSelf` / `unionSelfChecked` / `difference` / `xor`
 *   - `removeSelfIntersectionsPositive` / `removeSelfIntersectionsNegative`
 *   - `booleanOpPolyTree`
 *   - `polyTreeToPaths64`
 *
 * The `KlipperZ_*` Z-callback variants exist in the bundle but are not used by these tests.
 */

import type { KlipPath64, KlipPaths64, KlipPolyTree64 } from './adapter';
import type { ClipType, FillRule } from './test-data-parser';

// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore -- bundle has no accompanying .d.ts
import * as KlipModule from '../_dist/Klip.mjs';



interface KlipBundle {
  Klipper_booleanOp: KlipApi['booleanOp'];
  Klipper_booleanOpPolyTree: KlipApi['booleanOpPolyTree'];
  Klipper_polyTreeToPaths64: KlipApi['polyTreeToPaths64'];
  Klipper_intersect: KlipApi['intersect'];
  Klipper_union: KlipApi['union'];
  Klipper_unionSelf: KlipApi['unionSelf'];
  Klipper_unionSelfChecked: KlipApi['unionSelfChecked'];
  Klipper_difference: KlipApi['difference'];
  Klipper_xor: KlipApi['xor'];
  Klipper_removeSelfIntersectionsPositive: KlipApi['removeSelfIntersectionsPositive'];
  Klipper_removeSelfIntersectionsNegative: KlipApi['removeSelfIntersectionsNegative'];
}

interface KlipApi {
  booleanOp(
    clipType: ClipType,
    subject: KlipPaths64,
    clip: KlipPaths64 | null,
    fillRule: FillRule,
  ): KlipPaths64;

  booleanOpPolyTree(
    clipType: ClipType,
    subject: KlipPaths64,
    clip: KlipPaths64 | null,
    fillRule: FillRule,
  ): KlipPolyTree64;

  polyTreeToPaths64(polyTree: KlipPolyTree64): KlipPaths64;

  intersect(clip: KlipPaths64, subject: KlipPaths64): KlipPaths64;
  union(clip: KlipPaths64, subject: KlipPaths64): KlipPaths64;
  unionSelf(subject: KlipPaths64): KlipPaths64;
  unionSelfChecked(subject: KlipPaths64): KlipPaths64;
  difference(clip: KlipPaths64, subject: KlipPaths64): KlipPaths64;
  xor(clip: KlipPaths64, subject: KlipPaths64): KlipPaths64;
  removeSelfIntersectionsPositive(subject: KlipPath64): KlipPaths64;
  removeSelfIntersectionsNegative(subject: KlipPath64): KlipPaths64;

}

const bundle = KlipModule as unknown as KlipBundle;

export const Klip: KlipApi = {
  booleanOp: bundle.Klipper_booleanOp,
  booleanOpPolyTree: bundle.Klipper_booleanOpPolyTree,
  polyTreeToPaths64: bundle.Klipper_polyTreeToPaths64,
  intersect: bundle.Klipper_intersect,
  union: bundle.Klipper_union,
  unionSelf: bundle.Klipper_unionSelf,
  unionSelfChecked: bundle.Klipper_unionSelfChecked,
  difference: bundle.Klipper_difference,
  xor: bundle.Klipper_xor,
  removeSelfIntersectionsPositive: bundle.Klipper_removeSelfIntersectionsPositive,
  removeSelfIntersectionsNegative: bundle.Klipper_removeSelfIntersectionsNegative
};

export type { KlipPath64, KlipPaths64, KlipPolyTree64 };
