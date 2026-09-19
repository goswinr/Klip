import { describe, expect, test } from 'vitest';
import { Klip } from './klip-api';
import { areaPaths, makePath, PointInPolygonResult } from './adapter';
// Exercise the actual Fable predicates, not the independent test adapter's PIP.
// @ts-ignore -- Fable JavaScript has no accompanying .d.ts
import { Geo_pointInPolygon, Geo_segsIntersectNotInclusive, Geo_isColinear, Geo_dotProductSign } from '../_js/Src/Core.js';
// @ts-ignore -- Fable JavaScript has no accompanying .d.ts
import { pointInOpPolygon, areaTriangle, areaOutPt } from '../_js/Src/EngineUtil.js';
// @ts-ignore -- generated Fable module
import { Path64$1__get_SignedArea } from '../_js/Src/Core.js';
// @ts-ignore -- Fable JavaScript has no accompanying .d.ts
import * as Engine from '../_js/Src/Engine.js';

describe('Float topology and tolerance defaults', () => {
  test('translated thin triangles retain their signed area in paths and rings', () => {
    const b = 1e12;
    const p = makePath([b, b, b+1000, b+1000, b+500, b+500+0.0001]);
    expect(Path64$1__get_SignedArea(p)).toBe(0.06103515625);
    expect(areaTriangle(...p.xys)).toBe(0.1220703125);
    const ring: any[] = Array.from({ length: 3 }, (_, i) => ({ x: p.xys[2*i], y: p.xys[2*i+1] }));
    ring.forEach((p, i) => { p.next = ring[(i+1)%3]; p.prev = ring[(i+2)%3]; });
    expect(areaOutPt(ring[0])).toBe(0.1220703125);
  });
  test.each([1e-200, 1e-90, 1, 1e90, 1e200])('angle predicates retain their meaning at scale %s', s => {
    expect(Geo_isColinear(1e-6, 0, 0, s, 0, s, s)).toBe(false);
    expect(Geo_isColinear(1e-6, 0, 0, s, s, 0, 0)).toBe(true);
    expect(Geo_dotProductSign(0, 0, s, s, 0, 0)).toBe(-1);
    expect(Geo_dotProductSign(0, 0, s, s, 2*s, 2*s)).toBe(1);
  });
  test('angular cleanup preserves a large thin triangle beyond the distance tolerance', () => {
    const result = Klip.unionSelf([makePath([0, 0, 1e6, 1e6, 500000, 500001])]);
    expect(result).toHaveLength(1);
    expect(areaPaths(result)).toBe(500000);
  });
  test.each([1, 0.01])('default union preserves a triangle with side %s', side => {
    const result = Klip.unionSelf([makePath([0, 0, side, 0, 0, side])]);
    expect(result).toHaveLength(1);
    expect(result[0].xys).toHaveLength(6);
    expect(areaPaths(result)).toBeCloseTo(side * side / 2, 12);
  });

  test('assigning the reported tolerance does not change any default threshold', () => {
    const c = Engine.Clipper64$1_$ctor();
    const thresholds = () => [c.coordEqTol, c.mergeVertexTolerance, c.nearTopYToleranceCap, c.smallTriangleTol, c.splitAreaTol];
    const before = thresholds();
    Engine.Clipper64$1__set_Tolerance_5E38073B(c, Engine.Clipper64$1__get_Tolerance(c));
    expect(thresholds()).toEqual(before);
  });

  test.each([
    { x: 100000, y: 100100, expected: PointInPolygonResult.IsInside },
    { x: 100100, y: 100000, expected: PointInPolygonResult.IsOutside },
    { x: 1000, y: 1010, expected: PointInPolygonResult.IsInside },
    { x: 1000, y: 990, expected: PointInPolygonResult.IsOutside },
  ])('long-edge containment of ($x,$y) is invariant under winding and start vertex', ({ x, y, expected }) => {
    const points = [[0, 0], [1e6, 1e6], [0, 1e6]];
    for (const winding of [points, [...points].reverse()]) {
      for (let start = 0; start < points.length; start++) {
        const rotated = [...winding.slice(start), ...winding.slice(0, start)];
        expect(Geo_pointInPolygon(1e-5, x, y, makePath(rotated.flat()))).toBe(expected);
        const ring = rotated.map(([x, y]) => ({ x, y, next: null, prev: null })) as any[];
        ring.forEach((p, i) => { p.next = ring[(i + 1) % ring.length]; p.prev = ring[(i + ring.length - 1) % ring.length]; });
        expect(pointInOpPolygon(1e-5, x, y, ring[0])).toBe(expected);
      }
    }
  });

  test('boundary tolerance measures distance on both sloped and vertical edges', () => {
    expect(Geo_pointInPolygon(1e-5, 5, 5 + 0.5e-5, makePath([0, 0, 10, 10, 0, 10]))).toBe(PointInPolygonResult.IsOn);
    expect(Geo_pointInPolygon(1e-5, 5, 5 + 2e-5, makePath([0, 0, 10, 10, 0, 10]))).toBe(PointInPolygonResult.IsInside);
    const square = makePath([0, 0, 10, 0, 10, 10, 0, 10]);
    expect(Geo_pointInPolygon(1e-5, 10 + 0.5e-5, 5, square)).toBe(PointInPolygonResult.IsOn);
    expect(Geo_pointInPolygon(1e-5, 10 + 2e-5, 5, square)).toBe(PointInPolygonResult.IsOutside);
  });

  test('zero tolerance recognizes exact collinearity without direction normalization error', () => {
    expect(Geo_pointInPolygon(0, 1, 49, makePath([0, 0, 3, 147, 0, 147]))).toBe(PointInPolygonResult.IsOn);
  });

  test.each([0, 1e-20, 1e-16, 1e-12, 1e-5])('exact edge incidence survives boundary tolerance %s', tolerance => {
    expect(Geo_pointInPolygon(tolerance, 1, 49, makePath([0, 0, 3, 147, 0, 147]))).toBe(PointInPolygonResult.IsOn);
    expect(Geo_pointInPolygon(tolerance, 1, 49, makePath([0, 147, 3, 147, 0, 0]))).toBe(PointInPolygonResult.IsOn);
  });

  test('a short segment crosses a long diagonal, while endpoint-only contact is excluded', () => {
    expect(Geo_segsIntersectNotInclusive(0, 0, 1e6, 1e6, 5e5, 5e5 - 1, 5e5, 5e5 + 1)).toBe(true);
    expect(Geo_segsIntersectNotInclusive(0, 0, 1e6, 1e6, 5e5, 5e5, 5e5, 5e5 + 1)).toBe(false);
  });
});
