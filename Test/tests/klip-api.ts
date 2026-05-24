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
 *   - `inflate` / `inflatePaths` / `offsetOpenPaths`
 *
 * Z-callback variants exist in the bundle but are not used by these tests.
 */

import type { KlipPath64, KlipPaths64, KlipPolyTree64 } from './adapter';
import type { ClipType, FillRule } from './test-data-parser';

// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore -- bundle has no accompanying .d.ts
import * as KlipModule from '../_dist/Klip.mjs';

// Mirrors Klip's `Offset.JoinType` (Src/Offset.fs).
// NOTE: numeric values differ from clipper2-ts — keep tests parameterized via
// these constants, not raw integers.
export enum JoinType {
  Miter = 0,
  Square = 1,
  Bevel = 2,
  Round = 3,
}

// Mirrors Klip's `Offset.EndType` (Src/Offset.fs).
export enum EndType {
  Polygon = 0,
  Joined = 1,
  Butt = 2,
  Square = 3,
  Round = 4,
}

interface KlipBundle {
  Klipper_booleanOp: KlipApi['booleanOp'];
  Klipper_booleanOpWithPolyTree: KlipApi['booleanOpWithPolyTree'];
  Klipper_polyTreeToPaths64: KlipApi['polyTreeToPaths64'];
  Klipper_intersect: KlipApi['intersect'];
  Klipper_union: KlipApi['union'];
  Klipper_unionSelf: KlipApi['unionSelf'];
  Klipper_difference: KlipApi['difference'];
  Klipper_xor: KlipApi['xor'];
  Klipper_inflate: KlipApi['inflate'];
  Klipper_inflatePaths: KlipApi['inflatePaths'];
  Klipper_offsetOpenPaths: KlipApi['offsetOpenPaths'];
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

  /** Round joins, miter limit 2.0, default arc tolerance. EndType is Polygon. */
  inflate(delta: number, paths: KlipPaths64): KlipPaths64;

  /**
   * Inflate (positive `delta`) or deflate (negative `delta`) closed polygons.
   * EndType is implicitly Polygon. `arcTolerance` of 0 means automatic.
   */
  inflatePaths(
    paths: KlipPaths64,
    delta: number,
    joinType: JoinType,
    miterLimit: number,
    arcTolerance: number,
  ): KlipPaths64;

  /** Offsets open paths with the specified join and end type. */
  offsetOpenPaths(
    paths: KlipPaths64,
    delta: number,
    joinType: JoinType,
    endType: EndType,
    miterLimit: number,
    arcTolerance: number,
  ): KlipPaths64;
}

const bundle = KlipModule as unknown as KlipBundle;

export const Klip: KlipApi = {
  booleanOp: bundle.Klipper_booleanOp,
  booleanOpWithPolyTree: bundle.Klipper_booleanOpWithPolyTree,
  polyTreeToPaths64: bundle.Klipper_polyTreeToPaths64,
  intersect: bundle.Klipper_intersect,
  union: bundle.Klipper_union,
  unionSelf: bundle.Klipper_unionSelf,
  difference: bundle.Klipper_difference,
  xor: bundle.Klipper_xor,
  inflate: bundle.Klipper_inflate,
  inflatePaths: bundle.Klipper_inflatePaths,
  offsetOpenPaths: bundle.Klipper_offsetOpenPaths,
};

export type { KlipPath64, KlipPaths64, KlipPolyTree64 };
