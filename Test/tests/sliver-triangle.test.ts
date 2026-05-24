// Sliver triangle union bug regression test — mirrors
// `clipper2-ts/tests/sliver-triangle.test.ts` (originally from
// https://github.com/AngusJohnson/Clipper2/issues/1067).
//
// The bug: a NonZero union of a subject triangle with several clip triangles
// (two of them slivers with near-zero area) produced an output polygon with
// total area far exceeding the sum of input areas — i.e. created region
// outside any input shape.

import { describe, test, expect } from 'vitest';
import { Klip } from './klip-api';
import { ClipType, FillRule } from './test-data-parser';
import { toKlipPaths, area, type KlipPath64 } from './adapter';

describe('Sliver triangle union bug (Issue #1067)', () => {
  test('union with sliver triangles should not produce area outside input polygons', () => {
    const poly1 = toKlipPaths([
      [
        { x: -45077288, y: -27835646 },
        { x: -45216220, y: -27853069 },
        { x: -44996290, y: -28378125 },
      ],
    ]);

    const poly2 = toKlipPaths([
      // Sliver
      [
        { x: -45943111, y: -27944226 },
        { x: -45990276, y: -27890686 },
        { x: -46034753, y: -27840198 },
      ],
      // Sliver
      [
        { x: -44185329, y: -29939581 },
        { x: -45679436, y: -28243538 },
        { x: -47826654, y: -25806113 },
      ],
      // Big triangle
      [
        { x: -48000000, y: -29000000 },
        { x: -44185329, y: -29939581 },
        { x: -47826654, y: -25806113 },
      ],
      // Small triangle
      [
        { x: -45679436, y: -28243538 },
        { x: -45514581, y: -27890485 },
        { x: -45943111, y: -27944226 },
      ],
    ]);

    const absSum = (paths: KlipPath64[]) =>
      paths.reduce((acc, p) => acc + Math.abs(area(p)), 0);

    const inputArea = absSum(poly1) + absSum(poly2);

    const result = Klip.booleanOp(ClipType.Union, poly1, poly2, FillRule.NonZero, undefined);

    const resultArea = absSum(result);

    // 1% tolerance for rounding — same as the clipper2-ts test.
    const tolerance = inputArea * 0.01;
    expect(resultArea).toBeLessThanOrEqual(inputArea + tolerance);
  });
});
