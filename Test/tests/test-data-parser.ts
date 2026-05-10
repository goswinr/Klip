/**
 * Test data parser for Clipper2 test files.
 *
 * Mirrors clipper2-ts/tests/test-data-parser.ts but is self-contained:
 * defines local `ClipType`/`FillRule` enums and the `Point64` shape so this
 * module has no dependency on Klip's compiled output. Numeric values match
 * Klip's `Engine1.ClipType` / `Engine1.FillRule`.
 */

import { readFileSync } from 'fs';
import { resolve } from 'path';

// Mirrors Klip's Engine1.ClipType discriminator values.
export enum ClipType {
  NoClip = 0,
  Intersection = 1,
  Union = 2,
  Difference = 3,
  Xor = 4,
}

// Mirrors Klip's Engine1.FillRule discriminator values.
export enum FillRule {
  EvenOdd = 0,
  NonZero = 1,
  Positive = 2,
  Negative = 3,
}

export interface Point64 {
  readonly x: number;
  readonly y: number;
}

export type PtPath64 = Point64[];
export type PtPaths64 = PtPath64[];

export interface TestCase {
  readonly caption: string;
  readonly clipType: ClipType;
  readonly fillRule: FillRule;
  readonly expectedArea: number;
  readonly expectedCount: number;
  readonly subjects: PtPaths64;
  readonly subjectsOpen: PtPaths64;
  readonly clips: PtPaths64;
}

export class TestDataParser {
  public static loadTestCase(filename: string, testNumber: number): TestCase | null {
    try {
      const fullPath = resolve(__dirname, 'test-data', filename);
      const content = readFileSync(fullPath, 'utf-8');
      const lines = content.split(/\r?\n/);
      return this.parseTestCase(lines, testNumber);
    } catch (error) {
      console.error(`Failed to load test file ${filename}:`, error);
      return null;
    }
  }

  private static parseTestCase(lines: string[], targetTestNumber: number): TestCase | null {
    let currentTestNumber = 0;
    let i = 0;

    while (i < lines.length) {
      if (lines[i].startsWith('CAPTION:')) {
        currentTestNumber++;
        if (currentTestNumber === targetTestNumber) {
          return this.parseTestCaseContent(lines, i);
        }
      }
      i++;
    }

    return null;
  }

  private static parseTestCaseContent(lines: string[], startIndex: number): TestCase | null {
    let caption = '';
    let clipType = ClipType.Intersection;
    let fillRule = FillRule.EvenOdd;
    let expectedArea = 0;
    let expectedCount = 0;
    let subjects: PtPaths64 = [];
    let subjectsOpen: PtPaths64 = [];
    let clips: PtPaths64 = [];

    let i = startIndex;

    while (i < lines.length) {
      const line = lines[i].trim();

      if (line.startsWith('CAPTION:') && i > startIndex) {
        break;
      }

      if (line.startsWith('CAPTION:')) {
        caption = line.substring(8).trim();
      } else if (line.startsWith('CLIPTYPE:')) {
        clipType = this.parseClipType(line);
      } else if (line.startsWith('FILLRULE:')) {
        fillRule = this.parseFillRule(line);
      } else if (line.startsWith('SOL_AREA:')) {
        expectedArea = parseInt(line.substring(9).trim()) || 0;
      } else if (line.startsWith('SOL_COUNT:')) {
        expectedCount = parseInt(line.substring(10).trim()) || 0;
      } else if (line.startsWith('SUBJECTS_OPEN')) {
        const result = this.parsePaths(lines, i + 1);
        subjectsOpen = result.paths;
        i = result.nextIndex - 1;
      } else if (line.startsWith('SUBJECTS')) {
        const result = this.parsePaths(lines, i + 1);
        subjects = result.paths;
        i = result.nextIndex - 1;
      } else if (line.startsWith('CLIPS')) {
        const result = this.parsePaths(lines, i + 1);
        clips = result.paths;
        i = result.nextIndex - 1;
      }

      i++;
    }

    return {
      caption,
      clipType,
      fillRule,
      expectedArea,
      expectedCount,
      subjects,
      subjectsOpen,
      clips,
    };
  }

  public static parseClipType(line: string): ClipType {
    const upperLine = line.toUpperCase();
    if (upperLine.includes('INTERSECTION')) return ClipType.Intersection;
    if (upperLine.includes('UNION')) return ClipType.Union;
    if (upperLine.includes('DIFFERENCE')) return ClipType.Difference;
    if (upperLine.includes('XOR')) return ClipType.Xor;
    return ClipType.NoClip;
  }

  public static parseFillRule(line: string): FillRule {
    const upperLine = line.toUpperCase();
    if (upperLine.includes('EVENODD')) return FillRule.EvenOdd;
    if (upperLine.includes('NONZERO')) return FillRule.NonZero;
    if (upperLine.includes('POSITIVE')) return FillRule.Positive;
    if (upperLine.includes('NEGATIVE')) return FillRule.Negative;
    return FillRule.EvenOdd;
  }

  private static parsePaths(lines: string[], startIndex: number): { paths: PtPaths64; nextIndex: number } {
    const paths: PtPaths64 = [];
    let i = startIndex;

    while (i < lines.length) {
      const line = lines[i].trim();

      if (!line ||
          line.startsWith('CAPTION:') ||
          line.startsWith('CLIPTYPE:') ||
          line.startsWith('FILLRULE:') ||
          line.startsWith('SOL_') ||
          line.startsWith('SUBJECTS') ||
          line.startsWith('CLIPS')) {
        break;
      }

      const path = this.parseCoordinateLine(line);
      if (path && path.length > 0) {
        paths.push(path);
      }

      i++;
    }

    return { paths, nextIndex: i };
  }

  private static parseCoordinateLine(line: string): PtPath64 | null {
    const path: PtPath64 = [];
    const coords = line.split(/[,\s]+/).filter(s => s.trim().length > 0);

    if (coords.length % 2 !== 0) {
      return null;
    }

    for (let i = 0; i < coords.length; i += 2) {
      const x = parseInt(coords[i]);
      const y = parseInt(coords[i + 1]);

      if (isNaN(x) || isNaN(y)) {
        return null;
      }

      path.push({ x, y });
    }

    return path.length > 0 ? path : null;
  }

  public static loadAllTestCases(filename: string): TestCase[] {
    try {
      const fullPath = resolve(__dirname, 'test-data', filename);
      const content = readFileSync(fullPath, 'utf-8');
      const lines = content.split(/\r?\n/);

      const testCases: TestCase[] = [];
      let testNumber = 1;

      while (true) {
        const testCase = this.parseTestCase(lines, testNumber);
        if (!testCase) break;

        testCases.push(testCase);
        testNumber++;
      }

      return testCases;
    } catch (error) {
      console.error(`Failed to load test file ${filename}:`, error);
      return [];
    }
  }
}
