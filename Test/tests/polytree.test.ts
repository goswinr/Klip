// PolyTree Hierarchical Structure Tests — mirrors clipper2-ts/tests/polytree.test.ts
// adapted to Klip's `booleanOpPolyTree` (returns the tree) + free-function tree helpers.

import { describe, test, expect } from 'vitest';
import { Klip } from './klip-api';
import { ClipType, FillRule, Point64, TestDataParser } from './test-data-parser';
import {
  toKlipPaths,
  makePath,
  areaPaths,
  pointInPolygon,
  PointInPolygonResult,
  treeArea,
  treeCount,
  treeChild,
  polyPathIsHole,
  type KlipPath64,
  type KlipPolyTree64,
  type KlipPaths64,
} from './adapter';

interface PolyPathLike {
  polygon: KlipPath64 | null;
  children: PolyPathLike[];
}

describe('PolyTree Hierarchical Structure Tests', () => {
  function polyPathContainsPoint(pp: PolyPathLike, pt: Point64): { result: boolean; counter: number } {
    let counter = 0;

    if (pp.polygon && pp.polygon.xys.length > 0) {
      const hit = pointInPolygon(pt, pp.polygon);
      if (hit !== PointInPolygonResult.IsOutside) {
        if (polyPathIsHole(pp as KlipPolyTree64)) counter--;
        else counter++;
      }
    }

    for (let i = 0; i < pp.children.length; i++) {
      const childResult = polyPathContainsPoint(pp.children[i], pt);
      counter += childResult.counter;
    }

    return { result: counter !== 0, counter };
  }

  function polytreeContainsPoint(pp: KlipPolyTree64, pt: Point64): boolean {
    let counter = 0;
    for (let i = 0; i < treeCount(pp); i++) {
      const result = polyPathContainsPoint(treeChild(pp, i), pt);
      counter += result.counter;
    }
    expect(counter).toBeGreaterThanOrEqual(0);
    return counter !== 0;
  }

  function polyPathFullyContainsChildren(pp: PolyPathLike): boolean {
    for (let i = 0; i < pp.children.length; i++) {
      const child = pp.children[i];
      if (!child.polygon) continue;

      // Every child vertex must lie on or inside the parent contour.
      const xys = child.polygon.xys;
      for (let j = 0; j < (xys.length >>> 1); j++) {
        if (!pp.polygon) return false;
        const coord = j * 2;
        const pt = { x: xys[coord], y: xys[coord + 1] };
        if (pointInPolygon(pt, pp.polygon) === PointInPolygonResult.IsOutside) {
          return false;
        }
      }

      if (child.children.length > 0 && !polyPathFullyContainsChildren(child)) {
        return false;
      }
    }
    return true;
  }

  function checkPolytreeFullyContainsChildren(polytree: KlipPolyTree64): boolean {
    for (let i = 0; i < treeCount(polytree); i++) {
      const child = treeChild(polytree, i);
      if (treeCount(child) > 0 && !polyPathFullyContainsChildren(child)) {
        return false;
      }
    }
    return true;
  }

  test('should correctly establish hole ownership relationships', () => {
    const testCase = TestDataParser.loadTestCase('PolytreeHoleOwner2.txt', 1);
    expect(testCase).not.toBeNull();

    const subj = toKlipPaths(testCase!.subjects);
    const clip = toKlipPaths(testCase!.clips);

    const solutionTree = Klip.booleanOpPolyTree(
      testCase!.clipType,
      subj,
      clip.length > 0 ? clip : null,
      testCase!.fillRule,
    );

    const solutionPaths: KlipPaths64 = Klip.polyTreeToPaths64(solutionTree);
    const area1 = Math.abs(areaPaths(solutionPaths));
    const area2 = Math.abs(treeArea(solutionTree));

    expect(area1).toBeGreaterThan(330000);
    expect(Math.abs(area1 - area2)).toBeLessThan(0.0001);

    expect(checkPolytreeFullyContainsChildren(solutionTree)).toBe(true);

    const pointsOfInterestOutside: Point64[] = [
      { x: 21887, y: 10420 },
      { x: 21726, y: 10825 },
      { x: 21662, y: 10845 },
      { x: 21617, y: 10890 },
    ];

    const pointsOfInterestInside: Point64[] = [
      { x: 21887, y: 10430 },
      { x: 21843, y: 10520 },
      { x: 21810, y: 10686 },
      { x: 21900, y: 10461 },
    ];

    for (const pt of pointsOfInterestOutside) {
      expect(polytreeContainsPoint(solutionTree, pt)).toBe(false);
    }
    for (const pt of pointsOfInterestInside) {
      expect(polytreeContainsPoint(solutionTree, pt)).toBe(true);
    }
  });

  test('should handle complex polygon nesting correctly', () => {
    const subjects: KlipPaths64 = [];

    subjects.push(makePath([1588700, -8717600, 1616200, -8474800, 1588700, -8474800]));

    subjects.push(makePath([
      13583800, -15601600, 13582800, -15508500, 13555300, -15508500,
      13555500, -15182200, 13010900, -15185400,
    ]));

    subjects.push(makePath([956700, -3092300, 1152600, 3147400, 25600, 3151700]));

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
      1003900, -630100, 1253300, -12284500, 12983400, -16239900,
    ]));

    subjects.push(makePath([198200, 12149800, 1010600, 12149800, 1011500, 11859600]));
    subjects.push(makePath([21996700, -7432000, 22096700, -7432000, 22096700, -7332000]));

    const solutionTree = Klip.booleanOpPolyTree(
      ClipType.Union,
      subjects,
      null,
      FillRule.NonZero,
    );

    // Expected nesting: 1 outer with 2 holes, one hole has 1 nested polygon.
    expect(treeCount(solutionTree)).toBe(1);
    expect(treeCount(treeChild(solutionTree, 0))).toBe(2);
    expect(treeCount(treeChild(treeChild(solutionTree, 0), 1))).toBe(1);
  });

  test('should calculate PolyTree areas correctly', () => {
    const outer = makePath([0, 0, 100, 0, 100, 100, 0, 100]);
    const hole = makePath([20, 20, 80, 20, 80, 80, 20, 80]);
    const inner = makePath([30, 30, 70, 30, 70, 70, 30, 70]);

    const subjects: KlipPaths64 = [outer, hole, inner];

    const solutionTree = Klip.booleanOpPolyTree(
      ClipType.Union,
      subjects,
      null,
      FillRule.EvenOdd,
    );

    // outer (10000) - hole (3600) + inner (1600) = 8000
    const expectedArea = 10000 - 3600 + 1600;
    const calculatedArea = Math.abs(treeArea(solutionTree));
    expect(Math.abs(calculatedArea - expectedArea)).toBeLessThan(100);
  });
});
