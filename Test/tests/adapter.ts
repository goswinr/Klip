/**
 * Adapter between clipper2-ts-style `{x, y}[]` paths and Klip's flat-buffer
 * `Path64` shape, plus a few geometry helpers.
 *
 * Klip's `Path64` (see Klip/Src/Core.fs) is a class with `xys: number[]`
 * interleaved as `[x0, y0, x1, y1, ...]`, and optional `zs: any[]`, but the
 * runtime implementation is fully duck-typed: every accessor in the bundled code
 * reads `.xys` directly and `Path64__Add_Z6A810145` only touches `zs` when it
 * is not null. So a plain object literal `{ xys }` is interchangeable with a real
 * Path64 for the purposes of these tests.
 *
 * Klip's compiled bundle (`_dist/Klip.mjs`) does not surface `Path64.SignedArea`
 * / `pointInPolygon` / PolyTree accessors — they get tree-shaken because the
 * exposed boolean ops don't reference them. So we re-implement those helpers
 * here using the same shoelace / ray-casting logic.
 */

import type { Point64 } from './test-data-parser';

// Klip's runtime Path64 shape.
export interface KlipPath64 {
  xys: number[];
  zs?: any[];
}

export type KlipPaths64 = KlipPath64[];

// Construct a Klip-shaped Path64 from clipper2-ts `{x, y}[]`.
export function toKlipPath(pts: Point64[]): KlipPath64 {
  const xys = new Array<number>(pts.length * 2);
  for (let i = 0; i < pts.length; i++) {
    const coord = i * 2;
    xys[coord] = pts[i].x;
    xys[coord + 1] = pts[i].y;
  }
  return { xys };
}

export function toKlipPaths(paths: Point64[][]): KlipPaths64 {
  const out: KlipPaths64 = new Array(paths.length);
  for (let i = 0; i < paths.length; i++) out[i] = toKlipPath(paths[i]);
  return out;
}

// Mirrors clipper2-ts's `Clipper.makePath` — builds a single closed path from
// a flat `[x1, y1, x2, y2, ...]` coordinate list.
export function makePath(xys: number[]): KlipPath64 {
  if (xys.length % 2 !== 0) {
    throw new Error(`makePath: odd coordinate count (${xys.length})`);
  }
  return { xys: xys.slice() };
}

// Convert a Klip Path64 back to clipper2-ts `{x, y}[]`.
export function fromKlipPath(p: KlipPath64): Point64[] {
  const out: Point64[] = new Array(p.xys.length >>> 1);
  for (let i = 0; i < out.length; i++) {
    const coord = i * 2;
    out[i] = { x: p.xys[coord], y: p.xys[coord + 1] };
  }
  return out;
}

export function fromKlipPaths(ps: KlipPaths64): Point64[][] {
  return ps.map(fromKlipPath);
}

/**
 * Signed area of a single closed path. Mirrors Klip's `Path64.SignedArea`
 * member (Core.fs:234) — same shoelace formulation.
 */
export function area(path: KlipPath64): number {
  const cnt = path.xys.length >>> 1;
  if (cnt < 3) return 0;
  const xys = path.xys;
  let total = 0;
  const prevCoord = (cnt - 1) * 2;
  let prevX = xys[prevCoord];
  let prevY = xys[prevCoord + 1];
  for (let i = 0; i < cnt; i++) {
    const coord = i * 2;
    const x = xys[coord];
    const y = xys[coord + 1];
    total += (prevY + y) * (prevX - x);
    prevX = x;
    prevY = y;
  }
  return total * 0.5;
}

export function areaPaths(paths: KlipPaths64): number {
  let total = 0;
  for (const p of paths) total += area(p);
  return total;
}

export enum PointInPolygonResult {
  IsOn = 0,
  IsInside = 1,
  IsOutside = 2,
}

/**
 * Standard crossing-number / ray-casting point-in-polygon.
 * Used by polytree.test.ts to mirror clipper2-ts's `Clipper.pointInPolygon`.
 */
export function pointInPolygon(pt: Point64, polygon: KlipPath64): PointInPolygonResult {
  const xys = polygon.xys;
  const n = xys.length >>> 1;
  if (n < 3) return PointInPolygonResult.IsOutside;

  let inside = false;
  let j = n - 1;
  for (let i = 0; i < n; i++) {
    const icoord = i * 2;
    const jcoord = j * 2;
    const xi = xys[icoord], yi = xys[icoord + 1];
    const xj = xys[jcoord], yj = xys[jcoord + 1];

    if ((xi === pt.x && yi === pt.y)) return PointInPolygonResult.IsOn;

    if ((yi > pt.y) !== (yj > pt.y)) {
      const xCross = (xj - xi) * (pt.y - yi) / (yj - yi) + xi;
      if (pt.x === xCross) return PointInPolygonResult.IsOn;
      if (pt.x < xCross) inside = !inside;
    }
    j = i;
  }

  return inside ? PointInPolygonResult.IsInside : PointInPolygonResult.IsOutside;
}

// ---------- PolyTree helpers ----------
//
// Klip's PolyTree64 / PolyPath64 (Engine1.fs) at runtime is a plain object:
//   { children: PolyPath64[], _parent: PolyPath64 | null, polygon: Path64 | null }
// `booleanOpWithPolyTree` mutates `children` in place. The class has methods
// like `count`, `child(i)`, `isHole`, `area()` in the F# definition, but those
// are tree-shaken from the bundle (only the boolean ops + polyTreeToPaths64
// are exported). So we provide free-function equivalents here.

export interface KlipPolyPath64 {
  children: KlipPolyPath64[];
  _parent: KlipPolyPath64 | null;
  polygon: KlipPath64 | null;
}

export type KlipPolyTree64 = KlipPolyPath64;

export function newPolyTree(): KlipPolyTree64 {
  return { children: [], _parent: null, polygon: null };
}

export function treeCount(pp: KlipPolyPath64): number {
  return pp.children.length;
}

export function treeChild(pp: KlipPolyPath64, index: number): KlipPolyPath64 {
  if (index < 0 || index >= pp.children.length) {
    throw new Error(`PolyPath64.child: index ${index} out of range for ${pp.children.length} children`);
  }
  return pp.children[index];
}

// Level: number of parents up to the root. Root has level 0; its children
// (outer contours) are level 1; their children (holes) are level 2; etc.
export function polyPathLevel(pp: KlipPolyPath64): number {
  let level = 0;
  let cur = pp._parent;
  while (cur !== null) {
    level++;
    cur = cur._parent;
  }
  return level;
}

// A node is a hole when its level is even and > 0 (i.e. a child of a hole, or
// equivalently a path nested inside an outer contour). Mirrors Klip's
// `PolyPath64.IsHole` (Engine1.fs).
export function polyPathIsHole(pp: KlipPolyPath64): boolean {
  const lvl = polyPathLevel(pp);
  return lvl !== 0 && (lvl & 1) === 0;
}

// Sum of (signed-then-flipped-for-holes) area across a node and its descendants.
// Mirrors Klip's `PolyPath64.Area` total.
export function treeArea(pp: KlipPolyPath64): number {
  let total = pp.polygon ? area(pp.polygon) : 0;
  for (const c of pp.children) total += treeArea(c);
  return total;
}
