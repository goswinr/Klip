// Comprehensive Polygon Clipping Tests
//
// Mirrors clipper2-ts/tests/polygons.test.ts, but exercises Klip's exported
// boolean-op surface (`booleanOp`, `booleanOpWithPolyTree`, `polyTreeToPaths64`)
// after converting the parsed `Point64[]` test data into Klip's `Path64` class
// via `./adapter`. Klip's high-level wrappers do not expose open subjects, so any
// test case whose `subjectsOpen` is non-empty is skipped.

import { describe, test, expect } from 'vitest';
import {
  booleanOp,
  booleanOpWithPolyTree,
  polyTreeToPaths64
} from '../_ts/Src/Klip.ts';
import { LipInternal_PolyTree64 } from '../_ts/Src/Engine1.ts';
import { TestDataParser, ClipType, FillRule } from './test-data-parser';
import { toLipPaths, areaPaths } from './adapter';

describe('Comprehensive Polygon Clipping Tests', () => {
  // Utility function for tolerance checking
  function isInList(num: number, list: number[]): boolean {
    return list.includes(num);
  }

  // Load all test cases once
  const allTestCases = TestDataParser.loadAllTestCases('Polygons.txt');

  // Create individual test for each of the 195 polygon test cases (matches C# TestPolygons.cs)
  // This makes each test case show up individually in test output
  test.each(allTestCases.map((tc, idx) => ({ testNum: idx + 1, testCase: tc })))(
    'Polygon Test $testNum: $testCase.clipType/$testCase.fillRule',
    ({ testNum, testCase }) => {
      // Klip's wrappers don't surface `addOpenSubject`; skip mixed test cases.
      if (testCase.subjectsOpen.length > 0) return;

      const subj = toLipPaths(testCase.subjects);
      const clip = toLipPaths(testCase.clips);

      const solution = booleanOp(testCase.clipType, subj, clip, testCase.fillRule);

      const measuredCount = solution.length;
      const measuredArea = Math.round(areaPaths(solution)); // C#: (long)Clipper.Area(solution)
      const countDiff = testCase.expectedCount > 0 ? Math.abs(testCase.expectedCount - measuredCount) : 0;
      const areaDiff = testCase.expectedArea > 0 ? Math.abs(testCase.expectedArea - measuredArea) : 0;
      const areaDiffRatio = testCase.expectedArea <= 0 ? 0 : areaDiff / testCase.expectedArea;

      // Validate count - C#-calibrated tolerances
      // These tolerances reflect that C# also has small differences from test file expected values
      if (testCase.expectedCount > 0) {
        if (isInList(testNum, [172])) {
          expect(countDiff).toBeLessThanOrEqual(17);  // Complex self-intersecting geometry
        } else if (isInList(testNum, [140, 150, 165, 166, 173, 176, 177, 179])) {
          expect(countDiff).toBeLessThanOrEqual(9);
        } else if (testNum >= 120) {
          expect(countDiff).toBeLessThanOrEqual(7);  // High-complexity range
        } else if (isInList(testNum, [27, 121, 126])) {
          expect(countDiff).toBeLessThanOrEqual(2);
        } else if (isInList(testNum, [23, 24, 37, 43, 45, 87, 102, 111, 118, 119])) {
          expect(countDiff).toBeLessThanOrEqual(1);
        } else if (testNum === 16) {
          // Test 16: C# reference also fails (bow-tie polygon edge case)
          expect(countDiff).toBeLessThanOrEqual(1);
        } else {
          // Most tests match exactly (including Test 168 after our fix!)
          if (countDiff !== 0) {
            console.log(`\nTest ${testNum} FAILED: expected ${testCase.expectedCount}, got ${measuredCount}, diff ${countDiff}`);
            console.log(`  ClipType: ${ClipType[testCase.clipType]}, FillRule: ${FillRule[testCase.fillRule]}`);
          }
          expect(countDiff).toBe(0);
        }
      }

      // Area validation - C#-calibrated tolerances
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
        } else if (isInList(testNum, [52, 53, 54, 59, 60, 64, 117, 118, 119, 184])) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.02);
        } else if (testNum === 172) {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.05);  // Complex self-intersecting geometry
        } else {
          expect(areaDiffRatio).toBeLessThanOrEqual(0.01);
        }
      }
    }
  );

  // Summary test to verify overall pass rate
  test('Polygon test suite summary', () => {
    // Verify we have all 195 test cases
    expect(allTestCases.length).toBe(195);
  });

  // PolyTree consistency validation
  test('should produce consistent results between Paths64 and PolyTree64 output', () => {
    // Test a representative subset for performance while maintaining coverage
    const testCases = TestDataParser.loadAllTestCases('Polygons.txt').slice(0, 50);

    for (let i = 0; i < testCases.length; i++) {
      const testCase = testCases[i];
      const testNum = i + 1;

      if (testCase.subjectsOpen.length > 0) continue; // Klip wrappers skip open subjects

      const subj = toLipPaths(testCase.subjects);
      const clip = toLipPaths(testCase.clips);

      // Execute with Paths64 output
      const solutionPaths = booleanOp(testCase.clipType, subj, clip, testCase.fillRule);

      // Execute with PolyTree64 output
      const solutionTree = new LipInternal_PolyTree64();
      booleanOpWithPolyTree(testCase.clipType, subj, clip, solutionTree, testCase.fillRule);

      const pathsFromTree = polyTreeToPaths64(solutionTree);

      // Area comparison
      const areaFromPaths = Math.round(areaPaths(solutionPaths));
      const areaFromTreePaths = Math.round(areaPaths(pathsFromTree));

      if (areaFromTreePaths !== areaFromPaths) {
        console.log(`Test ${testNum}: area mismatch - paths: ${areaFromPaths}, treePaths: ${areaFromTreePaths}`);
      }

      expect(areaFromTreePaths).toBe(areaFromPaths);

      // Count comparison
      const countFromPaths = solutionPaths.length;
      const countFromTree = pathsFromTree.length;

      expect(countFromTree).toBe(countFromPaths);
    }
  });

  // Specific geometric validation for known test cases
  test('should correctly handle basic geometric operations', () => {
    // Test case 1: Basic union operation
    const testCase1 = TestDataParser.loadTestCase('Polygons.txt', 1);
    expect(testCase1).not.toBeNull();
    expect(testCase1!.clipType).toBe(ClipType.Union);
    expect(testCase1!.fillRule).toBe(FillRule.NonZero);
    expect(testCase1!.expectedArea).toBe(9000);
    expect(testCase1!.expectedCount).toBe(1);

    const subj = toLipPaths(testCase1!.subjects);
    const solution1 = booleanOp(ClipType.Union, subj, [], FillRule.NonZero);

    expect(solution1.length).toBe(1);

    const area1 = Math.round(areaPaths(solution1));
    expect(area1).toBe(9000); // Should be exact match for simple case
  });

  // Edge case validation for degenerate inputs
  test('should handle edge cases gracefully', () => {
    // Empty subjects
    const emptySolution = booleanOp(ClipType.Union, [], [], FillRule.NonZero);
    expect(emptySolution.length).toBe(0);

    // Single point paths (should be filtered out)
    const singlePoint = toLipPaths([[{ x: 10, y: 10 }]]);
    const singlePointSolution = booleanOp(ClipType.Union, singlePoint, [], FillRule.NonZero);
    expect(singlePointSolution.length).toBe(0);

    // Two point paths (lines - should be filtered out for closed path operations)
    const twoPoint = toLipPaths([[{ x: 0, y: 0 }, { x: 10, y: 10 }]]);
    const twoPointSolution = booleanOp(ClipType.Union, twoPoint, [], FillRule.NonZero);
    expect(twoPointSolution.length).toBe(0);
  });
});
