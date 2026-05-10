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
    zCallback: undefined,
  ): KlipPaths64;

  booleanOpWithPolyTree(
    clipType: number,
    subject: KlipPaths64,
    clip: KlipPaths64 | null,
    polyTree: KlipPolyTree64,
    fillRule: number,
    zCallback: undefined,
  ): void;

  polyTreeToPaths64(polyTree: KlipPolyTree64): KlipPaths64;
}

export const Klip = KlipModule as unknown as KlipApi;

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

export function newPolyTree(): KlipPolyTree64 {
  return { children: [], _parent: null, polygon: null };
}

export function clearPolyTree(polyTree: KlipPolyTree64): void {
  polyTree.children.length = 0;
  polyTree._parent = null;
  polyTree.polygon = null;
}

export function union(
  subject: KlipPaths64,
  fillRule: FillRule = FillRule.NonZero,
  clip: KlipPaths64 | null = null,
): KlipPaths64 {
  return Klip.booleanOp(ClipType.Union, subject, clip, fillRule, undefined);
}

export function intersect(
  subject: KlipPaths64,
  clip: KlipPaths64,
  fillRule: FillRule = FillRule.NonZero,
): KlipPaths64 {
  return Klip.booleanOp(ClipType.Intersection, subject, clip, fillRule, undefined);
}

export function difference(
  subject: KlipPaths64,
  clip: KlipPaths64,
  fillRule: FillRule = FillRule.NonZero,
): KlipPaths64 {
  return Klip.booleanOp(ClipType.Difference, subject, clip, fillRule, undefined);
}

export function xor(
  subject: KlipPaths64,
  clip: KlipPaths64,
  fillRule: FillRule = FillRule.NonZero,
): KlipPaths64 {
  return Klip.booleanOp(ClipType.Xor, subject, clip, fillRule, undefined);
}
