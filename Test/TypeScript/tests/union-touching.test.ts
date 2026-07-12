// Touching-polygon union regression tests - mirror the F# BooleanTests
// `UnionTwoTouchingPolygons*` cases (Klip/Test/FSharp/Tests/Tests1/Tests/BooleanTests.fs).
//
// Two polygons that share an edge along a line carrying floating-point noise
// (the `…6262690989118975…` seam) must union into a SINGLE path with exactly
// 8 points. Before the colinear test was rescaled to an edge-length basis
// (Klip/Src/Core.fs `crossIsZero`), the near-horizontal seam left a redundant
// colinear spike vertex (9 points) and, for the negative-coordinate case,
// even prevented the two polygons from merging at all (2 paths).

import { describe, test, expect } from 'vitest';
import { Klip } from './klip-api';
import { makePath, areaPaths, type KlipPath64 } from './adapter';

function pointCount(p: KlipPath64): number {
  return p.xys.length >>> 1;
}

// Klipper.union takes (clip, subject); each is a Paths64 (array of Path64).
function unionToSinglePath(subjectXYs: number[], clipXYs: number[]): KlipPath64[] {
  return Klip.union([makePath(clipXYs)], [makePath(subjectXYs)]);
}

describe('Union of two touching polygons → single 8-point path', () => {
  test('positive coordinates', () => {
    const solution = unionToSinglePath(
      [3, 4, 5, 4, 5, 6.262690989118975, 3, 6.262690989118975],
      [5, 5, 7, 5, 7, 7, 4, 7, 4, 6.262690989118976, 5, 6.262690989118975],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  test('negative coordinates', () => {
    const solution = unionToSinglePath(
      [
        -3.0000000000000004, -3.9999999999999996,
        -5.000000000000001, -3.9999999999999996,
        -5.000000000000001, -6.262690989118974,
        -3.000000000000001, -6.262690989118975,
      ],
      [
        -5.000000000000001, -4.999999999999999,
        -7.000000000000001, -4.999999999999999,
        -7.000000000000001, -6.999999999999999,
        -4.000000000000001, -6.999999999999999,
        -4.000000000000001, -6.262690989118975,
        -5.000000000000001, -6.262690989118974,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  test('large coordinates', () => { // fails because input polines don't share start and end vertex?
    const solution = unionToSinglePath(
      [
        -40000, 30000.000000000004,
        -40000, 50000,
        -62626.90989118975, 50000.00000000001,
        -62626.90989118975, 30000.000000000004,
      ],
      [
        -50000, 50000,
        -49999.99999999999, 70000,
        -70000, 70000,
        -70000, 40000.00000000001,
        -62626.90989118976, 40000.00000000001,
        -62626.90989118975, 50000.00000000001,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  // Two polygons touching along a SHARED colinear edge (x=-0.05 vertical overlap
  // plus the y=-0.0626 seam), at sub-unit scale.
  test('tiny coordinates, shared colinear edge', () => {
    const solution = unionToSinglePath(
      [
        -0.030000000000000002, -0.039999999999999994,
        -0.05000000000000001, -0.039999999999999994,
        -0.05000000000000001, -0.06262690989118976,
        -0.030000000000000006, -0.06262690989118976,
      ],
      [
        -0.05000000000000001, -0.049999999999999996,
        -0.07, -0.049999999999999996,
        -0.07000000000000002, -0.06999999999999999,
        -0.04000000000000001, -0.07,
        -0.04000000000000001, -0.06262690989118977,
        -0.05000000000000001, -0.06262690989118976,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  test('scale 0.01, rotated 180deg', () => {
    const solution = unionToSinglePath(
      [
        -0.030000000000000002, -0.039999999999999994,
        -0.05000000000000001, -0.039999999999999994,
        -0.05000000000000001, -0.06262690989118976,
        -0.030000000000000006, -0.06262690989118976,
      ],
      [
        -0.05000000000000001, -0.049999999999999996,
        -0.07, -0.049999999999999996,
        -0.07000000000000002, -0.06999999999999999,
        -0.04000000000000001, -0.07,
        -0.04000000000000001, -0.06262690989118977,
        -0.05000000000000001, -0.06262690989118976,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  test('scale 0.01, rotated -90deg', () => {
    const solution = unionToSinglePath(
      [
        0.353, -0.030000000000000065,
        0.353, -0.050000000000000065,
        0.3756269098911898, -0.05000000000000007,
        0.3756269098911898, -0.03000000000000007,
      ],
      [
        0.363, -0.05000000000000007,
        0.363, -0.07000000000000008,
        0.383, -0.07000000000000008,
        0.383, -0.04000000000000007,
        0.3756269098911898, -0.04000000000000007,
        0.3756269098911898, -0.05000000000000007,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  test('scale 0.1, rotated 180deg', () => {
    const solution = unionToSinglePath(
      [
        -14.090000000000002, -0.3999999999999983,
        -14.290000000000001, -0.39999999999999825,
        -14.290000000000001, -0.6262690989118957,
        -14.090000000000002, -0.6262690989118957,
      ],
      [
        -14.290000000000001, -0.4999999999999982,
        -14.490000000000002, -0.4999999999999982,
        -14.490000000000002, -0.6999999999999983,
        -14.190000000000001, -0.6999999999999983,
        -14.190000000000001, -0.6262690989118959,
        -14.290000000000001, -0.6262690989118957,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  test('scale 1, rotated -90deg', () => {
    const solution = unionToSinglePath(
      [
        35.3, -3.0000000000000067,
        35.3, -5.000000000000006,
        37.56269098911898, -5.000000000000007,
        37.56269098911898, -3.000000000000007,
      ],
      [
        36.3, -5.000000000000007,
        36.3, -7.000000000000007,
        38.3, -7.000000000000007,
        38.3, -4.000000000000007,
        37.56269098911898, -4.000000000000007,
        37.56269098911898, -5.000000000000007,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  // The shared-edge shapes rotated ~179.999 deg: edges are now very slightly off-axis,
  // so no two edges are exactly colinear.
  test('rotated ~179.999deg, tiny coordinates', () => {
    const solution = unionToSinglePath(
      [
        -0.00030000698127131514, -0.00039999476395132075,
        -0.0005000069812408534, -0.00039999127329281686,
        -0.0005000109303816248, -0.0006262603721702517,
        -0.00030001093041208654, -0.0006262638628287556,
      ],
      [
        -0.0005000087265701053, -0.000499991273277586,
        -0.0007000087265396436, -0.0004999877826190821,
        -0.0007000122171981475, -0.0006999877825886203,
        -0.0004000122172438401, -0.0006999930185763761,
        -0.00040001093039685566, -0.0006262621174995036,
        -0.0005000109303816248, -0.0006262603721702517,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  test('rotated ~179.999deg, ~0.5 coordinates', () => {
    const solution = unionToSinglePath(
      [
        -0.30000698127131514, -0.3999947639513207,
        -0.5000069812408534, -0.39999127329281686,
        -0.5000109303816248, -0.6262603721702515,
        -0.3000109304120866, -0.6262638628287555,
      ],
      [
        -0.5000087265701053, -0.49999127327758597,
        -0.7000087265396436, -0.4999877826190821,
        -0.7000122171981474, -0.6999877825886204,
        -0.40001221724384006, -0.6999930185763762,
        -0.4000109303968557, -0.6262621174995037,
        -0.5000109303816248, -0.6262603721702515,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });

  test('tiny coordinates', () => {
    const solution = unionToSinglePath(
      [
        -0.004, 0.0030000000000000005,
        -0.004, 0.005,
        -0.0062626909891189755, 0.005,
        -0.0062626909891189755, 0.0030000000000000005,
      ],
      [
        -0.005, 0.005,
        -0.005, 0.007,
        -0.007, 0.007,
        -0.007, 0.004,
        -0.006262690989118976, 0.004,
        -0.0062626909891189755, 0.005,
      ],
    );
    expect(solution.length).toBe(1);
    expect(pointCount(solution[0])).toBe(8);
  });
});

// Mirror of the F# BooleanTests `UnionSelfNearHorizontalContinuationOfHorizontalEdge` case.
// A bound runs exactly horizontal (100,37)->(60,37) and then continues NEAR-horizontal
// (60,37)->(20,36.999999999), within the default HorizontalAngleTolerance. doHorizontal used
// to exit its loop on an exact next-vertex-Y comparison and leave the near-flat continuation
// orphaned in the AEL (no scanline for its top, never re-queued as a horizontal), so its curX
// evaluated to -Infinity at the next scanbeam and the union lost the left column of the ring
// (4 contours instead of 1).
describe('Horizontal bound continuing into a near-horizontal segment', () => {
  test('unionSelf returns one full-area contour', () => {
    const solution = Klip.unionSelf([
      makePath([
        0, 0,
        20, 0,
        20, 36.999999999,
        60, 37,
        100, 37,
        100, 100,
        0, 100,
      ]),
    ]);
    expect(solution.length).toBe(1);
    // full ring area: 100*63 above y=37 plus the 20-wide left column = 7040
    expect(Math.abs(areaPaths(solution))).toBeCloseTo(7040, 0);
  });
});

describe('Near-horizontal extrema from polysXY', () => {
  test('unionSelfChecked terminates for a single near-horizontal rectangle', () => {
    const solution = Klip.unionSelfChecked([
      makePath([
        17.823630670570378, 5.471326923838493,
        14.00086720159361, 5.471328036893782,
        14.000867265574863, 9.915292532211373,
        20.83405924248522, 9.915293067972888,
        20.83405950582065, 5.4713271767079,
        17.823630670570378, 5.471326923838493,
      ]),
    ]);

    expect(solution.length).toBe(1);
  });

  test('unionSelfChecked terminates for disjoint near-horizontal extrema', () => {
    const solution = Klip.unionSelfChecked([
      makePath([
        31.68114899807879, -3.1776702435233077,
        31.681147991813326, -5.232407663485913,
        29.05299627590947, -5.23240949946618,
        29.052995652930253, -3.177669208459603,
        31.68114899807879, -3.1776702435233077,
      ]),
      makePath([
        17.823630756706656, -3.1776745347575104,
        14.000866896598076, -3.177673366258442,
        14.000867801900924, -1.4574267995238073,
        20.834059175905757, -1.4574258437039758,
        20.834058932959618, -3.1776739283677022,
        17.823630756706656, -3.1776745347575104,
      ]),
    ]);

    expect(solution.length).toBe(2);
  });
});
