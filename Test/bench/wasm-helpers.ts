import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import type { Point64 } from 'clipper2-ts';
import type {
  ClipType as WasmClipType,
  FillRule as WasmFillRule,
  MainModule,
  Path64 as WasmPath64,
  Paths64 as WasmPaths64,
  PolyPath64 as WasmPolyTree64,
} from 'clipper2-wasm/dist/clipper2z';

// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore -- clipper2-wasm 0.2.1's package.json points `types` at a missing file.
import Clipper2ZFactory from 'clipper2-wasm';

const require = createRequire(import.meta.url);
const wasmBinary = readFileSync(require.resolve('clipper2-wasm/dist/es/clipper2z.wasm'));

export const Wasm = await Clipper2ZFactory({ wasmBinary }) as MainModule;

export type { WasmPaths64, WasmPolyTree64 };

export function toWasmPath(path: readonly Point64[]): WasmPath64 {
  const out = new Wasm.Path64();

  for (let i = 0; i < path.length; i++) {
    const point = new Wasm.Point64(BigInt(path[i].x), BigInt(path[i].y), 0n);
    out.push_back(point);
    point.delete();
  }

  return out;
}

export function toWasmPaths(paths: readonly (readonly Point64[])[]): WasmPaths64 {
  const out = new Wasm.Paths64();

  for (let i = 0; i < paths.length; i++) {
    const path = toWasmPath(paths[i]);
    out.push_back(path);
    path.delete();
  }

  return out;
}

export function newWasmPaths(): WasmPaths64 {
  return new Wasm.Paths64();
}

export function newWasmPolyTree(): WasmPolyTree64 {
  return new Wasm.PolyPath64();
}

export function wasmClipType(clipType: number): WasmClipType {
  switch (clipType) {
    case 1: return Wasm.ClipType.Intersection;
    case 2: return Wasm.ClipType.Union;
    case 3: return Wasm.ClipType.Difference;
    case 4: return Wasm.ClipType.Xor;
    default: throw new Error(`Unsupported ClipType ${clipType}`);
  }
}

export function wasmFillRule(fillRule: number): WasmFillRule {
  switch (fillRule) {
    case 0: return Wasm.FillRule.EvenOdd;
    case 1: return Wasm.FillRule.NonZero;
    case 2: return Wasm.FillRule.Positive;
    case 3: return Wasm.FillRule.Negative;
    default: throw new Error(`Unsupported FillRule ${fillRule}`);
  }
}

export function runWasmClipper(
  clipType: number,
  subject: WasmPaths64,
  clip: WasmPaths64 | null,
  fillRule: number,
): void {
  const clipper = new Wasm.Clipper64();
  const solution = new Wasm.Paths64();

  try {
    clipper.AddSubject(subject);
    if (clip !== null && clip.size() > 0) clipper.AddClip(clip);
    clipper.ExecutePath(wasmClipType(clipType), wasmFillRule(fillRule), solution);
  } finally {
    solution.delete();
    clipper.delete();
  }
}

export function runWasmPolyTree(
  clipType: number,
  subject: WasmPaths64,
  clip: WasmPaths64 | null,
  fillRule: number,
): void {
  const clipper = new Wasm.Clipper64();
  const polyTree = new Wasm.PolyPath64();

  try {
    clipper.AddSubject(subject);
    if (clip !== null && clip.size() > 0) clipper.AddClip(clip);
    clipper.ExecutePoly(wasmClipType(clipType), wasmFillRule(fillRule), polyTree);
  } finally {
    polyTree.delete();
    clipper.delete();
  }
}

export function wasmUnion(
  subject: WasmPaths64,
  fillRule: number,
  clip: WasmPaths64 | null = null,
): void {
  const solution = clip !== null && clip.size() > 0
    ? Wasm.Union64(subject, clip, wasmFillRule(fillRule))
    : Wasm.UnionSelf64(subject, wasmFillRule(fillRule));
  solution.delete();
}

export function wasmIntersect(subject: WasmPaths64, clip: WasmPaths64, fillRule: number): void {
  const solution = Wasm.Intersect64(subject, clip, wasmFillRule(fillRule));
  solution.delete();
}

export function wasmDifference(subject: WasmPaths64, clip: WasmPaths64, fillRule: number): void {
  const solution = Wasm.Difference64(subject, clip, wasmFillRule(fillRule));
  solution.delete();
}

export function wasmXor(subject: WasmPaths64, clip: WasmPaths64, fillRule: number): void {
  const solution = Wasm.Xor64(subject, clip, wasmFillRule(fillRule));
  solution.delete();
}
