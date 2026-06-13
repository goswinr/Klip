// Benchmark: at which input size should Clipper64 switch its pending-scanline
// container from the linear ScanlineArray to the ScanlineHeapSet?
//
// Run from Test/ (after `npm run build`):
//     node bench/scanline-threshold.mjs
//
// The knob under test is Klipper_setDefaultScanlineArrayThreshold(T):
//   - the array container is chosen when the local-minima count is <= T,
//   - and is upgraded to the heap mid-sweep when the pending scanline count grows past T.
// So T = 0 forces the heap+set everywhere and a huge T forces the array everywhere;
// values in between give the hybrid used in production. The crossover size where
// "always heap" starts beating "always array" is the threshold worth shipping.
//
// Workload: two k-by-k grids of slightly rotated squares, offset by half a cell,
// unioned with NonZero. Each rotated square contributes exactly one local minimum
// and all vertex Ys are distinct, so minima count = 2*k*k and scanline pressure is
// maximal for the geometry size. Results are checked to be identical across thresholds.


// Results 2026-06-11, node v26.2.0 (medians of >= 7 runs; the script asserts identical
// result areas across thresholds):

// rotated squares (all Ys distinct):
// ┌─────┬────────┬───────────┬───────────┬───────────┬───────────┬───────────┐
// │   k │ minima │ heap only │      T=16 │      T=64 │     T=256 │ array only│
// ├─────┼────────┼───────────┼───────────┼───────────┼───────────┼───────────┤
// │   2 │      8 │     0.011 │     0.009 │     0.009 │     0.009 │     0.009 │
// │   4 │     32 │     0.059 │     0.059 │     0.055 │     0.055 │     0.055 │
// │   8 │    128 │     0.426 │     0.429 │     0.428 │     0.440 │     0.438 │
// │  16 │    512 │     2.787 │     2.780 │     2.770 │     2.777 │     3.278 │
// │  32 │   2048 │    13.749 │    14.074 │    13.961 │    14.581 │    18.021 │
// │  64 │   8192 │    77.414 │    82.136 │    81.328 │    80.682 │   103.821 │
// └─────┴────────┴───────────┴───────────┴───────────┴───────────┴───────────┘
// upright diamonds (shared Ys per row, duplicate-heavy):
// ┌─────┬────────┬───────────┬───────────┬───────────┬───────────┬───────────┐
// │  16 │    512 │     0.513 │     0.515 │     0.519 │     0.515 │     0.532 │
// │  32 │   2048 │     2.632 │     2.672 │     2.644 │     2.675 │     2.816 │
// │  64 │   8192 │    14.746 │    15.024 │    15.413 │    15.533 │    16.565 │
// └─────┴────────┴───────────┴───────────┴───────────┴───────────┴───────────┘

// Reading: the array wins ~10–15% on tiny jobs, the heap wins 20–40% on big distinct-Y
// jobs, crossover ≈ 128 minima — and every hybrid threshold tracks the best pure mode,
// so the region is flat (32–256) and 64 sits comfortably in it. On duplicate-heavy input
// the array stays competitive at any size (few distinct pending Ys), so the minima-count
// heuristic is conservative there, costing only ~2–3%.
// The old 16-vs-64 inconsistency never mattered because both values were inside the flat zone.
//
// A set-free dedup-on-pop heap (push duplicates, discard equal roots in Pop) was measured
// against the shipped HashSet dedup and rejected: ~3-5% faster below 512 minima (where the
// heap is rarely active anyway), but ~10% slower at 8192 minima on distinct-Y input and
// 15-18% slower on the duplicate-heavy diamonds (each duplicate costs an extra siftDown).

import {
  Klipper_booleanOp,
  Klipper_setDefaultScanlineArrayThreshold,
  Klipper_getDefaultScanlineArrayThreshold,
} from "../_dist/Klip.mjs";

const CLIP_TYPE_UNION = 2; // ClipType.Union
const FILL_RULE_NONZERO = 1; // FillRule.NonZero

// One square rotated by `angle`, as a counter-clockwise flat-buffer path { xys }.
function rotatedSquare(cx, cy, halfDiagonal, angle) {
  const xys = new Array(8);
  for (let k = 0; k < 4; k++) {
    const a = angle + (k * Math.PI) / 2;
    xys[k * 2] = cx + halfDiagonal * Math.cos(a);
    xys[k * 2 + 1] = cy + halfDiagonal * Math.sin(a);
  }
  return { xys };
}

// k*k squares on a grid with `cell` spacing. With `rotate`, each square is rotated a
// little differently (deterministic, no PRNG) so no two vertices share a Y coordinate —
// maximal distinct scanlines, few duplicate insertScanline calls. Without `rotate`, all
// squares are upright diamonds, so every square in a row shares its three Y values —
// few distinct scanlines, maximal duplicate insertScanline calls (stresses the heap's
// dedup strategy).
function squareGrid(k, cell, offsetX, offsetY, rotate) {
  const paths = [];
  for (let row = 0; row < k; row++) {
    for (let col = 0; col < k; col++) {
      const angle = rotate ? 0.05 + 0.7133 * ((row * k + col) % 17) : 0; // irrational-ish spread
      paths.push(rotatedSquare(offsetX + col * cell, offsetY + row * cell, 0.62 * cell, angle));
    }
  }
  return paths;
}

function totalAbsArea(paths) {
  let total = 0;
  for (const p of paths) {
    const xys = p.xys;
    const cnt = xys.length >>> 1;
    if (cnt < 3) continue;
    let acc = 0;
    let prevX = xys[(cnt - 1) * 2];
    let prevY = xys[(cnt - 1) * 2 + 1];
    for (let i = 0; i < cnt; i++) {
      const x = xys[i * 2];
      const y = xys[i * 2 + 1];
      acc += (prevY + y) * (prevX - x);
      prevX = x;
      prevY = y;
    }
    total += acc * 0.5;
  }
  return total;
}

// Median time per op in ms: warm up, then repeat until >= minTotalMs and >= minRuns.
function timeOp(run, minTotalMs = 300, minRuns = 7) {
  for (let i = 0; i < 3; i++) run();
  const samples = [];
  const start = performance.now();
  while (samples.length < minRuns || performance.now() - start < minTotalMs) {
    const t0 = performance.now();
    run();
    samples.push(performance.now() - t0);
  }
  samples.sort((a, b) => a - b);
  return samples[samples.length >> 1];
}

const ALWAYS_ARRAY = 1 << 30;
const thresholds = [0, 16, 64, 256, ALWAYS_ARRAY];
const gridSizes = [2, 4, 8, 16, 32, 64]; // minima count = 2*k*k

const label = (t) =>
  t === 0 ? "heap only" : t === ALWAYS_ARRAY ? "array only" : `T=${t}`;

console.log(`node ${process.version}, default threshold = ${Klipper_getDefaultScanlineArrayThreshold()}`);

for (const rotate of [true, false]) {
  console.log(
    rotate
      ? "\nunion of two offset k×k grids of rotated squares (all Ys distinct), NonZero; median ms per op"
      : "\nunion of two offset k×k grids of upright diamonds (shared Ys per row), NonZero; median ms per op"
  );
  console.log(
    ["   k", "minima", ...thresholds.map((t) => label(t).padStart(11))].join("  ")
  );

  for (const k of gridSizes) {
    const cell = 10;
    const subject = squareGrid(k, cell, 0, 0, rotate);
    const clip = squareGrid(k, cell, cell / 2, cell / 2, rotate);

    let referenceArea = null;
    const row = [String(k).padStart(4), String(2 * k * k).padStart(6)];
    for (const t of thresholds) {
      Klipper_setDefaultScanlineArrayThreshold(t);
      const solution = Klipper_booleanOp(CLIP_TYPE_UNION, subject, clip, FILL_RULE_NONZERO);
      const a = totalAbsArea(solution);
      if (referenceArea === null) referenceArea = a;
      else if (Math.abs(a - referenceArea) > 1e-6 * Math.abs(referenceArea)) {
        throw new Error(`threshold ${t} changed the result for k=${k}: area ${a} vs ${referenceArea}`);
      }
      const ms = timeOp(() => Klipper_booleanOp(CLIP_TYPE_UNION, subject, clip, FILL_RULE_NONZERO));
      row.push(ms.toFixed(3).padStart(11));
    }
    console.log(row.join("  "));
  }
}

Klipper_setDefaultScanlineArrayThreshold(64); // restore the shipped default
