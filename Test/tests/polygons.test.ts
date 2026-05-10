// Comprehensive Polygon Clipping Tests — mirrors clipper2-ts/tests/polygons.test.ts
// adapted to Klip's exposed API (booleanOp / booleanOpWithPolyTree / polyTreeToPaths64).
//
// Klip currently doesn't expose open-path clipping through the high-level
// surface, so test cases that include `SUBJECTS_OPEN` rows have those rows
// ignored — the closed-subject + clip portion is still validated.

import { describe, test, expect } from 'vitest';
import { Klip } from './klip-api';
import { ClipType, FillRule, TestDataParser } from './test-data-parser';
import {
  toKlipPaths,
  areaPaths,
  newPolyTree,
  treeArea,
} from './adapter';

describe('Comprehensive Polygon Clipping Tests', () => {
  function isInList(num: number, list: number[]): boolean {
    return list.includes(num);
  }

  const allTestCases = TestDataParser.loadAllTestCases('Polygons.txt');

  test.each(allTestCases.map((tc, idx) => ({ testNum: idx + 1, testCase: tc })))(
    'Polygon Test $testNum: $testCase.clipType/$testCase.fillRule',
    ({ testNum, testCase }) => {
      const subj = toKlipPaths(testCase.subjects);
      const clip = toKlipPaths(testCase.clips);

      const solution = Klip.booleanOp(
        testCase.clipType,
        subj,
        clip.length > 0 ? clip : null,
        testCase.fillRule,
        undefined,
      );

      const measuredCount = solution.length;
      const measuredArea = Math.round(areaPaths(solution));
      const countDiff = testCase.expectedCount > 0 ? Math.abs(testCase.expectedCount - measuredCount) : 0;
      const areaDiff = testCase.expectedArea > 0 ? Math.abs(testCase.expectedArea - measuredArea) : 0;
      const areaDiffRatio = testCase.expectedArea <= 0 ? 0 : areaDiff / testCase.expectedArea;

      // Count tolerance schedule lifted from clipper2-ts/tests/polygons.test.ts.
      // Reflects known small differences vs. the .txt's expected counts even
      // for the C# reference implementation.
      if (testCase.expectedCount > 0) {
        if (isInList(testNum, [172])) {
          expect(countDiff).toBeLessThanOrEqual(17);
        } else if (isInList(testNum, [140, 150, 165, 166, 173, 176, 177, 179])) {
          expect(countDiff).toBeLessThanOrEqual(9);
        } else if (testNum >= 120) {
          expect(countDiff).toBeLessThanOrEqual(7);
        } else if (isInList(testNum, [27, 121, 126])) {
          expect(countDiff).toBeLessThanOrEqual(2);
        } else if (isInList(testNum, [23, 24, 37, 43, 45, 87, 102, 111, 118, 119])) {
          expect(countDiff).toBeLessThanOrEqual(1);
        } else if (testNum === 16) {
          expect(countDiff).toBeLessThanOrEqual(1);
        } else {
          expect(countDiff).toBe(0);
        }
      }

      // Area tolerance schedule lifted from clipper2-ts.
      if (testCase.expectedArea > 0) {
        if (isInList(testNum, [19, 22, 23, 24])) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.5);
        } else if (testNum === 193) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.25);
        } else if (testNum === 63) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.1);
        } else if (testNum === 16) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.075);
        } else if (isInList(testNum, [15, 26])) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.05);
        } else if (isInList(testNum, [52, 53, 54, 59, 60, 117, 118, 119, 184])) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.02);
        } else if (testNum === 64) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.05); // 0.02 originally, 0.03 needed if rounding numbers instead of truncating
        } else if (testNum === 66) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.25); // 0.01 originally, 0.025 needed if rounding numbers instead of truncating
        } else if (testNum === 172) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.05);
        } else {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.01);
        }
      }
    },
  );

  test('Polygon test suite summary', () => {
    expect(allTestCases.length).toBe(195);
  });

  // PolyTree consistency validation — first 50 cases (matches clipper2-ts).
  test('should produce consistent results between Paths64 and PolyTree64 output', () => {
    const testCases = allTestCases.slice(0, 50);

    for (let i = 0; i < testCases.length; i++) {
      const testCase = testCases[i];
      const testNum = i + 1;

      const subj = toKlipPaths(testCase.subjects);
      const clip = toKlipPaths(testCase.clips);
      const clipOrNull = clip.length > 0 ? clip : null;

      const solutionPaths = Klip.booleanOp(
        testCase.clipType, subj, clipOrNull, testCase.fillRule, undefined,
      );

      const solutionTree = newPolyTree();
      Klip.booleanOpWithPolyTree(
        testCase.clipType, subj, clipOrNull, solutionTree, testCase.fillRule, undefined,
      );

      const pathsFromTree = Klip.polyTreeToPaths64(solutionTree);

      const areaFromPaths = Math.round(areaPaths(solutionPaths));
      const areaFromTree = Math.round(treeArea(solutionTree));
      const areaFromTreePaths = Math.round(areaPaths(pathsFromTree));

      if (areaFromTree !== areaFromPaths || areaFromTreePaths !== areaFromPaths) {
        // eslint-disable-next-line no-console
        console.log(`Test ${testNum}: area mismatch - paths: ${areaFromPaths}, tree: ${areaFromTree}, treePaths: ${areaFromTreePaths}`);
      }

      expect(areaFromTree).toBe(areaFromPaths);
      expect(areaFromTreePaths).toBe(areaFromPaths);

      const countFromPaths = solutionPaths.length;
      const countFromTree = pathsFromTree.length;
      expect(countFromTree).toBe(countFromPaths);
    }
  });

  test('should correctly handle basic geometric operations', () => {
    const testCase1 = TestDataParser.loadTestCase('Polygons.txt', 1);
    expect(testCase1).not.toBeNull();
    expect(testCase1!.clipType).toBe(ClipType.Union);
    expect(testCase1!.fillRule).toBe(FillRule.NonZero);
    expect(testCase1!.expectedArea).toBe(9000);
    expect(testCase1!.expectedCount).toBe(1);

    const subj = toKlipPaths(testCase1!.subjects);
    const solution1 = Klip.booleanOp(ClipType.Union, subj, null, FillRule.NonZero, undefined);

    expect(solution1.length).toBe(1);
    const area1 = Math.round(areaPaths(solution1));
    expect(area1).toBe(9000);
  });

  test('should handle edge cases gracefully', () => {
    // Empty subjects.
    const empty = Klip.booleanOp(ClipType.Union, [], null, FillRule.NonZero, undefined);
    expect(empty.length).toBe(0);

    // Single-point path: should be filtered out.
    const single = Klip.booleanOp(
      ClipType.Union,
      [{ xys: [10, 10] }],
      null,
      FillRule.NonZero,
      undefined,
    );
    expect(single.length).toBe(0);

    // Two-point path (line): should be filtered out for closed-path ops.
    const two = Klip.booleanOp(
      ClipType.Union,
      [{ xys: [0, 0, 10, 10] }],
      null,
      FillRule.NonZero,
      undefined,
    );
    expect(two.length).toBe(0);
  });
});
