// Polygon Offset Tests — mirrors clipper2-ts/tests/offsets.test.ts adapted
// to Klip's exposed offset surface (`inflate`, `inflatePaths`, `offsetOpenPaths`).
//
// The full `ClipperOffset` class is not exported by `_dist/Klip.mjs` (tree-shaken
// because the high-level wrappers don't reference it as an export), so:
//   - The "empty offset" test exercises `inflatePaths` with an empty input
//     rather than instantiating ClipperOffset directly.
//   - The "arc tolerance" test passes `arcTolerance` via `inflatePaths`'s
//     parameter rather than via a property setter.
// clipper2-ts uses `Clipper.inflatePathsD` (float-precision wrapper that scales
// internally); Klip's API is integer-only, so the zero-area-ring case rounds
// inputs ahead of time — same scenario, same expected behaviour.

import { describe, test, expect } from 'vitest';
import { Klip, JoinType, EndType, type KlipPaths64 } from './klip-api';
import { TestDataParser, type Point64 } from './test-data-parser';
import { toKlipPaths, area, areaPaths, makePath } from './adapter';

describe('Polygon Offset Tests', () => {
  test('should handle empty path offset operation', () => {
    const solution = Klip.inflatePaths([], 10, JoinType.Round, 2.0, 0.0);
    expect(solution).toHaveLength(0);
  });

  test('inflatePaths should not throw on a zero-area ring (all points identical)', () => {
    // clipper2-ts uses inflatePathsD with precision=2 and delta≈1.999. Klip's
    // API is integer-only, so pre-round to the integer equivalent (precision=2
    // means scale ×100, then round).
    const pts: Point64[] = [
      { x: Math.round(496.7798371623349 * 100), y: Math.round(253.05785493587112 * 100) },
      { x: Math.round(496.7798371623349 * 100), y: Math.round(253.05785493587112 * 100) },
      { x: Math.round(496.7798371623349 * 100), y: Math.round(253.05785493587112 * 100) },
      { x: Math.round(496.7798371623349 * 100), y: Math.round(253.05785493587112 * 100) },
    ];
    const scaledDelta = 1.9988052480000003 * 100;

    const solution = Klip.inflatePaths(
      toKlipPaths([pts]),
      scaledDelta,
      JoinType.Round,
      2.0,
      0.0,
    );

    expect(solution).toHaveLength(0);
  });

  test('should handle offset test cases from Offsets.txt', () => {
    const testCases = TestDataParser.loadAllTestCases('Offsets.txt');

    for (let i = 0; i < Math.min(testCases.length, 10); i++) {
      const testCase = testCases[i];

      const solution = Klip.inflatePaths(
        toKlipPaths(testCase.subjects),
        1,
        JoinType.Round,
        2.0,
        0.0,
      );

      if (solution.length > 0) {
        // Orientation consistency: there should be exactly one exterior contour
        // (sign matches the total signed area).
        const totalArea = areaPaths(solution);
        const isPositiveArea = totalArea > 0;

        let positiveCount = 0;
        let negativeCount = 0;
        for (const path of solution) {
          if (area(path) > 0) positiveCount++;
          else negativeCount++;
        }

        if (isPositiveArea) {
          expect(positiveCount).toBe(1);
        } else {
          expect(negativeCount).toBe(1);
        }
      }
    }
  });

  test('should respect arc tolerance in rounded offsets', () => {
    const scale = 10;
    const delta = 10 * scale;
    const arcTolerance = 0.25 * scale;

    const baseXys = [50, 50, 100, 50, 100, 150, 50, 150, 0, 100];
    const scaled = baseXys.map(v => v * scale);
    const scaledSubject: KlipPaths64 = [makePath(scaled)];

    const solution = Klip.inflatePaths(
      scaledSubject,
      delta,
      JoinType.Round,
      2.0,
      arcTolerance,
    );

    expect(solution).toHaveLength(1);

    const offsetPoints = solution[0].xys.length >>> 1;
    expect(offsetPoints).toBeLessThanOrEqual(21);

    if (solution.length > 0 && scaledSubject.length > 0) {
      const original = scaledSubject[0].xys;
      const offsetXys = solution[0].xys;
      const offsetCount = offsetXys.length >>> 1;
      const origCount = original.length >>> 1;

      let minDistance = Number.MAX_VALUE;
      let maxDistance = 0;

      for (let s = 0; s < origCount; s++) {
        const sx = original[s * 2];
        const sy = original[s * 2 + 1];

        for (let i = 0; i < offsetCount; i++) {
          const next = (i + 1) % offsetCount;
          const midX = (offsetXys[i * 2] + offsetXys[next * 2]) / 2;
          const midY = (offsetXys[i * 2 + 1] + offsetXys[next * 2 + 1]) / 2;

          const dx = midX - sx;
          const dy = midY - sy;
          const distance = Math.sqrt(dx * dx + dy * dy);

          if (distance < delta * 2) {
            if (distance < minDistance) minDistance = distance;
            if (distance > maxDistance) maxDistance = distance;
          }
        }
      }

      expect(minDistance + 1).toBeGreaterThanOrEqual(delta - arcTolerance);
    }
  });

  test('should handle negative offset operations correctly', () => {
    const subject: KlipPaths64 = [makePath([0, 0, 100, 0, 100, 100, 0, 100])];

    // Large negative offset should eliminate the polygon.
    let solution = Klip.inflatePaths(subject, -50, JoinType.Miter, 2.0, 0.0);
    expect(solution).toHaveLength(0);

    // Outer ring + interior hole.
    const subjectWithHole: KlipPaths64 = [
      makePath([0, 0, 100, 0, 100, 100, 0, 100]),
      makePath([40, 60, 60, 60, 60, 40, 40, 40]),
    ];

    solution = Klip.inflatePaths(subjectWithHole, 10, JoinType.Miter, 2.0, 0.0);
    expect(solution).toHaveLength(1);

    // Same shape with reversed orientations should also yield a single contour.
    const reverseXys = (xys: number[]): number[] => {
      const n = xys.length >>> 1;
      const out = new Array<number>(xys.length);
      for (let i = 0; i < n; i++) {
        const src = (n - 1 - i) * 2;
        out[i * 2] = xys[src];
        out[i * 2 + 1] = xys[src + 1];
      }
      return out;
    };
    const reversedSubject: KlipPaths64 = [
      { xys: reverseXys(subjectWithHole[0].xys) },
      { xys: reverseXys(subjectWithHole[1].xys) },
    ];

    solution = Klip.inflatePaths(reversedSubject, 10, JoinType.Miter, 2.0, 0.0);
    expect(solution).toHaveLength(1);
  });

  test('should produce correct results for different join types', () => {
    const subject: KlipPaths64 = [makePath([0, 0, 50, 0, 50, 50, 0, 50])];
    const delta = 10;

    let solution = Klip.inflatePaths(subject, delta, JoinType.Miter, 2.0, 0.0);
    expect(solution).toHaveLength(1);
    const miterVertexCount = solution[0].xys.length >>> 1;

    solution = Klip.inflatePaths(subject, delta, JoinType.Round, 2.0, 0.0);
    expect(solution).toHaveLength(1);
    expect(solution[0].xys.length >>> 1).toBeGreaterThanOrEqual(miterVertexCount);

    solution = Klip.inflatePaths(subject, delta, JoinType.Bevel, 2.0, 0.0);
    expect(solution).toHaveLength(1);

    solution = Klip.inflatePaths(subject, delta, JoinType.Square, 2.0, 0.0);
    expect(solution).toHaveLength(1);
  });

  test('should handle open path offsetting correctly', () => {
    const openPath: KlipPaths64 = [
      makePath([0, 50, 20, 50, 40, 50, 60, 50, 80, 50, 100, 50]),
    ];
    const delta = 10;

    let solution = Klip.offsetOpenPaths(openPath, delta, JoinType.Round, EndType.Butt, 2.0, 0.0);
    expect(solution).toHaveLength(1);

    solution = Klip.offsetOpenPaths(openPath, delta, JoinType.Round, EndType.Round, 2.0, 0.0);
    expect(solution).toHaveLength(1);

    solution = Klip.offsetOpenPaths(openPath, delta, JoinType.Round, EndType.Square, 2.0, 0.0);
    expect(solution).toHaveLength(1);

    for (const path of solution) {
      expect(path.xys.length >>> 1).toBeGreaterThanOrEqual(4);
    }
  });
});
