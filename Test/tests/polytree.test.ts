// PolyTree Hierarchical Structure Tests
//
// Mirrors clipper2-ts/tests/polytree.test.ts for Klip's `booleanOpWithPolyTree` +
// `polyTreeToPaths64` surface. PolyPath64 access goes through the Klip-internal
// accessors (`__get_Polygon`, `__get_Count`, `__Child_Z524259A4`, `__get_IsHole`,
// `__Area`); point-in-polygon checks use Klip's `Geo_pointInPolygon`.

import { describe, test, expect } from 'vitest';
import {
  booleanOpWithPolyTree,
  polyTreeToPaths64
} from '../_ts/Src/Klip.ts';
import {
  LipInternal_PolyTree64,
  LipInternal_PolyPath64,
  LipInternal_PolyPath64__get_Polygon,
  LipInternal_PolyPath64__get_Count,
  LipInternal_PolyPath64__get_IsHole,
  LipInternal_PolyPath64__Child_Z524259A4,
  LipInternal_PolyPath64__Area
} from '../_ts/Src/Engine1.ts';
import {
  Path64,
  Path64__get_Count,
  Path64__get_Xs,
  Path64__get_Ys,
  Geo_pointInPolygon
} from '../_ts/Src/Core.ts';
import { TestDataParser, ClipType, FillRule, Point64 } from './test-data-parser';
import { toLipPaths, areaPaths } from './adapter';

// PointInPolygonResult numeric values match Klip & clipper2-ts: IsOn=0, IsInside=1, IsOutside=2.
const PIP_IS_ON = 0;
const PIP_IS_OUTSIDE = 2;

// makePath helper mirroring `Clipper.makePath([x0, y0, x1, y1, ...])` for Klip Path64.
function makePath(coords: number[]): Path64 {
  const pts: Point64[] = [];
  for (let i = 0; i + 1 < coords.length; i += 2) {
    pts.push({ x: coords[i], y: coords[i + 1] });
  }
  return toLipPaths([pts])[0];
}

describe('PolyTree Hierarchical Structure Tests', () => {
  // Validates that PolyTree contains child polygons correctly
  function polyPathContainsPoint(pp: LipInternal_PolyPath64, pt: Point64): { result: boolean; counter: number } {
    let counter = 0;

    const polygon = LipInternal_PolyPath64__get_Polygon(pp);
    if (polygon && Path64__get_Count(polygon) > 0) {
      const pointInPoly = Geo_pointInPolygon(pt.x, pt.y, polygon);
      if (pointInPoly !== PIP_IS_OUTSIDE) {
        if (LipInternal_PolyPath64__get_IsHole(pp)) {
          counter--;
        } else {
          counter++;
        }
      }
    }

    const count = LipInternal_PolyPath64__get_Count(pp);
    for (let i = 0; i < count; i++) {
      const child = LipInternal_PolyPath64__Child_Z524259A4(pp, i);
      const childResult = polyPathContainsPoint(child, pt);
      counter += childResult.counter;
    }

    return { result: counter !== 0, counter };
  }

  // Validates point containment within PolyTree structure
  function polytreeContainsPoint(pp: LipInternal_PolyTree64, pt: Point64): boolean {
    let counter = 0;

    const count = LipInternal_PolyPath64__get_Count(pp);
    for (let i = 0; i < count; i++) {
      const child = LipInternal_PolyPath64__Child_Z524259A4(pp, i);
      const result = polyPathContainsPoint(child, pt);
      counter += result.counter;
    }

    expect(counter).toBeGreaterThanOrEqual(0); // Point can't be inside more holes than outers
    return counter !== 0;
  }

  // Validates that parent polygons fully contain their children
  function polyPathFullyContainsChildren(pp: LipInternal_PolyPath64): boolean {
    const count = LipInternal_PolyPath64__get_Count(pp);
    for (let i = 0; i < count; i++) {
      const child = LipInternal_PolyPath64__Child_Z524259A4(pp, i);
      const childPolygon = LipInternal_PolyPath64__get_Polygon(child);

      if (!childPolygon) continue;

      const parentPolygon = LipInternal_PolyPath64__get_Polygon(pp);
      if (!parentPolygon) return false;

      // Check if all vertices of child are inside parent
      const childCnt = Path64__get_Count(childPolygon);
      const xs = Path64__get_Xs(childPolygon);
      const ys = Path64__get_Ys(childPolygon);
      for (let j = 0; j < childCnt; j++) {
        const result = Geo_pointInPolygon(xs[j], ys[j], parentPolygon);
        if (result === PIP_IS_OUTSIDE) {
          return false;
        }
      }

      // Recursively check nested children
      if (LipInternal_PolyPath64__get_Count(child) > 0 && !polyPathFullyContainsChildren(child)) {
        return false;
      }
    }

    return true;
  }

  // Top-level PolyTree containment validation
  function checkPolytreeFullyContainsChildren(polytree: LipInternal_PolyTree64): boolean {
    const count = LipInternal_PolyPath64__get_Count(polytree);
    for (let i = 0; i < count; i++) {
      const child = LipInternal_PolyPath64__Child_Z524259A4(polytree, i);
      if (LipInternal_PolyPath64__get_Count(child) > 0 && !polyPathFullyContainsChildren(child)) {
        return false;
      }
    }
    return true;
  }

  // PolyTree hole ownership test (TestPolytree2 equivalent)
  test('should correctly establish hole ownership relationships', () => {
    const testCase = TestDataParser.loadTestCase('PolytreeHoleOwner2.txt', 1);
    expect(testCase).not.toBeNull();

    // Klip wrappers do not surface open subjects; this fixture uses none.
    expect(testCase!.subjectsOpen.length).toBe(0);

    const subj = toLipPaths(testCase!.subjects);
    const clip = toLipPaths(testCase!.clips);

    const solutionTree = new LipInternal_PolyTree64();
    booleanOpWithPolyTree(testCase!.clipType, subj, clip, solutionTree, testCase!.fillRule);

    // Validate expected geometric properties
    const solutionPaths = polyTreeToPaths64(solutionTree);
    const area1 = Math.abs(areaPaths(solutionPaths));
    const area2 = Math.abs(LipInternal_PolyPath64__Area(solutionTree));

    expect(area1).toBeGreaterThan(330000);
    expect(Math.abs(area1 - area2)).toBeLessThan(0.0001);

    // Validate hierarchical containment
    expect(checkPolytreeFullyContainsChildren(solutionTree)).toBe(true);

    // Test specific points
    const pointsOfInterestOutside: Point64[] = [
      { x: 21887, y: 10420 },
      { x: 21726, y: 10825 },
      { x: 21662, y: 10845 },
      { x: 21617, y: 10890 }
    ];

    const pointsOfInterestInside: Point64[] = [
      { x: 21887, y: 10430 },
      { x: 21843, y: 10520 },
      { x: 21810, y: 10686 },
      { x: 21900, y: 10461 }
    ];

    // Validate that outside points are correctly identified
    for (const pt of pointsOfInterestOutside) {
      expect(polytreeContainsPoint(solutionTree, pt)).toBe(false);
    }

    // Validate that inside points are correctly identified
    for (const pt of pointsOfInterestInside) {
      expect(polytreeContainsPoint(solutionTree, pt)).toBe(true);
    }
  });

  // Complex nesting validation (TestPolytree3 equivalent)
  test('should handle complex polygon nesting correctly', () => {
    const subjects: Path64[] = [];

    subjects.push(makePath([1588700, -8717600, 1616200, -8474800, 1588700, -8474800]));

    subjects.push(makePath([
      13583800, -15601600, 13582800, -15508500, 13555300, -15508500,
      13555500, -15182200, 13010900, -15185400
    ]));

    subjects.push(makePath([956700, -3092300, 1152600, 3147400, 25600, 3151700]));

    // Complete the polygon
    subjects.push(makePath([
      22575900, -16604000, 31286800, -12171900,
      31110200, 4882800, 30996200, 4826300, 30414400, 5447400, 30260000, 5391500,
      29662200, 5805400, 28844500, 5337900, 28435000, 5789300, 27721400, 5026400,
      22876300, 5034300, 21977700, 4414900, 21148000, 4654700, 20917600, 4653400,
      19334300, 12411000, -2591700, 12177200, 53200, 3151100, -2564300, 12149800,
      7819400, 4692400, 10116000, 5228600, 6975500, 3120100, 7379700, 3124700,
      11037900, 596200, 12257000, 2587800, 12257000, 596200, 15227300, 2352700,
      18444400, 1112100, 19961100, 5549400, 20173200, 5078600, 20330000, 5079300,
      20970200, 4544300, 20989600, 4563700, 19465500, 1112100, 21611600, 4182100,
      22925100, 1112200, 22952700, 1637200, 23059000, 1112200, 24908100, 4181200,
      27070100, 3800600, 27238000, 3800700, 28582200, 520300, 29367800, 1050100,
      29291400, 179400, 29133700, 360700, 29056700, 312600, 29121900, 332500,
      29269900, 162300, 28941400, 213100, 27491300, -3041500, 27588700, -2997800,
      22104900, -16142800, 13010900, -15603000, 13555500, -15182200,
      13555300, -15508500, 13582800, -15508500, 13583100, -15154700,
      1588700, -8822800, 1588700, -8379900, 1588700, -8474800, 1616200, -8474800,
      1003900, -630100, 1253300, -12284500, 12983400, -16239900
    ]));

    subjects.push(makePath([198200, 12149800, 1010600, 12149800, 1011500, 11859600]));
    subjects.push(makePath([21996700, -7432000, 22096700, -7432000, 22096700, -7332000]));

    const solutionTree = new LipInternal_PolyTree64();
    booleanOpWithPolyTree(ClipType.Union, subjects, [], solutionTree, FillRule.NonZero);

    // Validate the expected nesting structure: 1 outer with 2 holes, one hole has 1 nested polygon
    expect(LipInternal_PolyPath64__get_Count(solutionTree)).toBe(1);
    const child0 = LipInternal_PolyPath64__Child_Z524259A4(solutionTree, 0);
    expect(LipInternal_PolyPath64__get_Count(child0)).toBe(2);
    const child0_1 = LipInternal_PolyPath64__Child_Z524259A4(child0, 1);
    expect(LipInternal_PolyPath64__get_Count(child0_1)).toBe(1);
  });

  // PolyTree area calculation validation
  test('should calculate PolyTree areas correctly', () => {
    // Create nested rectangles: outer + hole + nested inner
    const outer = makePath([0, 0, 100, 0, 100, 100, 0, 100]);
    const hole = makePath([20, 20, 80, 20, 80, 80, 20, 80]);
    const inner = makePath([30, 30, 70, 30, 70, 70, 30, 70]);

    const subjects: Path64[] = [outer, hole, inner];

    const solutionTree = new LipInternal_PolyTree64();
    booleanOpWithPolyTree(ClipType.Union, subjects, [], solutionTree, FillRule.EvenOdd);

    // Calculate expected area: outer - hole + inner
    const expectedArea = 10000 - 3600 + 1600; // 8000
    const calculatedArea = Math.abs(LipInternal_PolyPath64__Area(solutionTree));

    expect(Math.abs(calculatedArea - expectedArea)).toBeLessThan(100); // Small tolerance for rounding
  });
});
