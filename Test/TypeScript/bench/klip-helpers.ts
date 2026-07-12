import { ClipType, FillRule, type Point64 } from 'clipper2-ts';

// `_dist/Klip.mjs` is the production Vite bundle. The callable surface below
// mirrors `../_tsc/Src/Klip.d.ts`, but uses Klip's duck-typed runtime path shape.
// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore -- the bundle intentionally ships without a sibling .d.ts file.
import * as KlipModule from '../_dist/Klip.mjs';

export interface KlipPath64 {
  xys: number[];
  zs?: unknown[];
}

export type KlipPaths64 = KlipPath64[];

export interface KlipPolyPath64 {
  children: KlipPolyPath64[];
  _parent: KlipPolyPath64 | null;
  polygon: KlipPath64 | null;
}

export type KlipPolyTree64 = KlipPolyPath64;

interface KlipApi {
  booleanOp(
    clipType: number,
    subject: KlipPaths64,
    clip: KlipPaths64 | null,
    fillRule: number,
  ): KlipPaths64;

  booleanOpPolyTree(
    clipType: number,
    subject: KlipPaths64,
    clip: KlipPaths64 | null,
    fillRule: number,
  ): KlipPolyTree64;

  polyTreeToPaths64(polyTree: KlipPolyTree64): KlipPaths64;
}

interface KlipBundle {
  Klipper_booleanOp: KlipApi['booleanOp'];
  Klipper_booleanOpPolyTree: KlipApi['booleanOpPolyTree'];
  Klipper_polyTreeToPaths64: KlipApi['polyTreeToPaths64'];
}

const bundle = KlipModule as unknown as KlipBundle;

export const Klip: KlipApi = {
  booleanOp: bundle.Klipper_booleanOp,
  booleanOpPolyTree: bundle.Klipper_booleanOpPolyTree,
  polyTreeToPaths64: bundle.Klipper_polyTreeToPaths64,
};

export function toKlipPath(path: readonly Point64[]): KlipPath64 {
  const xys = new Array<number>(path.length * 2);

  for (let i = 0; i < path.length; i++) {
    const coord = i * 2;
    xys[coord] = path[i].x;
    xys[coord + 1] = path[i].y;
  }

  return { xys };
}

export function toKlipPaths(paths: readonly (readonly Point64[])[]): KlipPaths64 {
  const out = new Array<KlipPath64>(paths.length);
  for (let i = 0; i < paths.length; i++) out[i] = toKlipPath(paths[i]);
  return out;
}

export function union(
  subject: KlipPaths64,
  fillRule: FillRule = FillRule.NonZero,
  clip: KlipPaths64 | null = null,
): KlipPaths64 {
  return Klip.booleanOp(ClipType.Union, subject, clip, fillRule);
}

export function intersect(
  subject: KlipPaths64,
  clip: KlipPaths64,
  fillRule: FillRule = FillRule.NonZero,
): KlipPaths64 {
  return Klip.booleanOp(ClipType.Intersection, subject, clip, fillRule);
}

export function difference(
  subject: KlipPaths64,
  clip: KlipPaths64,
  fillRule: FillRule = FillRule.NonZero,
): KlipPaths64 {
  return Klip.booleanOp(ClipType.Difference, subject, clip, fillRule);
}

export function xor(
  subject: KlipPaths64,
  clip: KlipPaths64,
  fillRule: FillRule = FillRule.NonZero,
): KlipPaths64 {
  return Klip.booleanOp(ClipType.Xor, subject, clip, fillRule);
}
