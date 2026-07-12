import { readFileSync } from 'node:fs';
import { bench, describe } from 'vitest';
import {
  Clipper,
  Clipper64,
  ClipType,
  FillRule,
  PolyTree64,
  type Path64,
  type Paths64,
} from 'clipper2-ts';
import { overlappingPairs, testData } from './test-data';
import {
  Klip,
  difference as klipDifference,
  intersect as klipIntersect,
  toKlipPaths,
  union as klipUnion,
} from './klip-helpers';
import {
  Wasm,
  newWasmPaths,
  newWasmPolyTree,
  runWasmClipper,
  runWasmPolyTree,
  toWasmPaths,
  wasmClipType,
  wasmDifference,
  wasmFillRule,
  wasmIntersect,
  wasmUnion,
} from './wasm-helpers';

const fillRule = FillRule.NonZero;

interface JsonPoint {
  x: number;
  y: number;
}

type RhinoPathsFixture = readonly (readonly (readonly JsonPoint[])[])[];

function loadRhinoPaths(scale: number): Paths64 {
  const fixture = JSON.parse(
    readFileSync(new URL('../../Rhino/polysXY.json', import.meta.url), 'utf8'),
  ) as RhinoPathsFixture;
  const paths: Paths64 = [];

  for (const group of fixture) {
    for (const path of group) {
      paths.push(path.map(point => ({
        x: Math.round(point.x * scale),
        y: Math.round(point.y * scale),
      })));
    }
  }

  return paths;
}

function runClipperTs(
  clipType: ClipType,
  subject: Paths64,
  clip: Paths64 | null,
): void {
  const c = new Clipper64();
  c.addSubject(subject);
  if (clip !== null && clip.length > 0) c.addClip(clip);

  const solution: Paths64 = [];
  c.execute(clipType, fillRule, solution);
}

function runClipperTsPolyTree(
  clipType: ClipType,
  subject: Paths64,
  clip: Paths64 | null,
): void {
  const c = new Clipper64();
  c.addSubject(subject);
  if (clip !== null && clip.length > 0) c.addClip(clip);

  const polytree = new PolyTree64();
  c.execute(clipType, fillRule, polytree);
}

function benchBooleanOperation(
  name: string,
  clipType: ClipType,
  subject: Paths64,
  clip: Paths64 | null = null,
  iterations = 10,
): void {
  const klipSubject = toKlipPaths(subject);
  const klipClip = clip === null ? null : toKlipPaths(clip);
  const wasmSubject = toWasmPaths(subject);
  const wasmClip = clip === null ? null : toWasmPaths(clip);

  describe(name, () => {
    bench('clipper2-ts', () => {
      for (let i = 0; i < iterations; i++) runClipperTs(clipType, subject, clip);
    });

    bench('clipper2-wasm', () => {
      for (let i = 0; i < iterations; i++) {
        runWasmClipper(clipType, wasmSubject, wasmClip, fillRule);
      }
    });

    bench('Klip', () => {
      for (let i = 0; i < iterations; i++) {
        Klip.booleanOp(clipType, klipSubject, klipClip, fillRule);
      }
    });

  });
}

function benchPolyTreeOperation(
  name: string,
  clipType: ClipType,
  subject: Paths64,
  clip: Paths64 | null = null,
): void {
  const klipSubject = toKlipPaths(subject);
  const klipClip = clip === null ? null : toKlipPaths(clip);
  const wasmSubject = toWasmPaths(subject);
  const wasmClip = clip === null ? null : toWasmPaths(clip);

  describe(name, () => {
    bench('clipper2-ts', () => {
      runClipperTsPolyTree(clipType, subject, clip);
    });

    bench('clipper2-wasm', () => {
      runWasmPolyTree(clipType, wasmSubject, wasmClip, fillRule);
    });

    bench('Klip', () => {
      Klip.booleanOpPolyTree(clipType, klipSubject, klipClip, fillRule);
    });

  });
}

function benchConvenienceOperation(
  name: string,
  runTs: () => void,
  runWasm: () => void,
  runKlip: () => void,
): void {
  describe(name, () => {
    bench('clipper2-ts', runTs);
    bench('clipper2-wasm', runWasm);
    bench('Klip', runKlip);
  });
}



describe('Intersection Operations', () => {
  benchBooleanOperation(
    'intersection - medium overlapping polygons',
    ClipType.Intersection,
    overlappingPairs.medium.subject,
    overlappingPairs.medium.clip,
  );
  benchBooleanOperation(
    'intersection - large overlapping polygons',
    ClipType.Intersection,
    overlappingPairs.large.subject,
    overlappingPairs.large.clip,
  );
  benchBooleanOperation(
    'intersection - grid with rectangle',
    ClipType.Intersection,
    overlappingPairs.grid.subject,
    overlappingPairs.grid.clip,
  );
});

describe('Difference Operations', () => {
  benchBooleanOperation(
    'difference - medium overlapping polygons',
    ClipType.Difference,
    overlappingPairs.medium.subject,
    overlappingPairs.medium.clip,
  );
  benchBooleanOperation(
    'difference - large overlapping polygons',
    ClipType.Difference,
    overlappingPairs.large.subject,
    overlappingPairs.large.clip,
  );
});

describe('XOR Operations', () => {
  benchBooleanOperation(
    'xor - medium overlapping polygons',
    ClipType.Xor,
    overlappingPairs.medium.subject,
    overlappingPairs.medium.clip,
  );
  benchBooleanOperation(
    'xor - large overlapping polygons',
    ClipType.Xor,
    overlappingPairs.large.subject,
    overlappingPairs.large.clip,
  );
});

describe('Multiple Operations (stress test)', () => {
  benchBooleanOperation('10 union operations on medium polygons', ClipType.Union, [testData.mediumComplex], null, 10);
  benchBooleanOperation(
    '10 intersection operations on medium polygons',
    ClipType.Intersection,
    overlappingPairs.medium.subject,
    overlappingPairs.medium.clip,
    10,
  );
});

describe('Convenience Functions', () => {
  {
    const subject = testData.mediumGrid;
    const klipSubject = toKlipPaths(subject);
    const wasmSubject = toWasmPaths(subject);

    benchConvenienceOperation(
      'Klipper.union - medium grid',
      () => { Clipper.union(subject, fillRule); },
      () => { wasmUnion(wasmSubject, fillRule); },
      () => { klipUnion(klipSubject, fillRule); },
    );
  }

  {
    const subject = overlappingPairs.medium.subject;
    const clip = overlappingPairs.medium.clip;
    const klipSubject = toKlipPaths(subject);
    const klipClip = toKlipPaths(clip);
    const wasmSubject = toWasmPaths(subject);
    const wasmClip = toWasmPaths(clip);

    benchConvenienceOperation(
      'Klipper.intersect - medium overlapping',
      () => { Clipper.intersect(subject, clip, fillRule); },
      () => { wasmIntersect(wasmSubject, wasmClip, fillRule); },
      () => { klipIntersect(klipSubject, klipClip, fillRule); },
    );

    benchConvenienceOperation(
      'Klipper.difference - medium overlapping',
      () => { Clipper.difference(subject, clip, fillRule); },
      () => { wasmDifference(wasmSubject, wasmClip, fillRule); },
      () => { klipDifference(klipSubject, klipClip, fillRule); },
    );
  }
});

describe('Simple Union Operations', () => {
  const simpleRects: Paths64 = [
    [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }, { x: 0, y: 100 }],
    [{ x: 150, y: 150 }, { x: 250, y: 150 }, { x: 250, y: 250 }, { x: 150, y: 250 }],
    [{ x: 300, y: 0 }, { x: 400, y: 0 }, { x: 400, y: 100 }, { x: 300, y: 100 }],
  ];

  const twoOverlapping: Paths64 = [
    [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }, { x: 0, y: 100 }],
    [{ x: 50, y: 50 }, { x: 150, y: 50 }, { x: 150, y: 150 }, { x: 50, y: 150 }],
  ];

  const simplePath: Path64 = [];
  for (let i = 0; i < 8; i++) {
    const angle = (i / 8) * Math.PI * 2;
    simplePath.push({
      x: Math.round(Math.cos(angle) * 50 + 100),
      y: Math.round(Math.sin(angle) * 50 + 100),
    });
  }

  const fourCircles: Paths64 = [
    simplePath,
    simplePath.map(p => ({ x: p.x + 200, y: p.y })),
    simplePath.map(p => ({ x: p.x, y: p.y + 200 })),
    simplePath.map(p => ({ x: p.x + 200, y: p.y + 200 })),
  ];

  benchBooleanOperation('union - 3 non-overlapping rectangles', ClipType.Union, simpleRects);
  benchBooleanOperation('union - 2 overlapping rectangles', ClipType.Union, twoOverlapping);
  benchBooleanOperation('union - 4 simple circles (no self-intersection)', ClipType.Union, fourCircles);
});

describe('PolyTree Operations', () => {
  benchPolyTreeOperation('polytree - medium grid union', ClipType.Union, testData.mediumGrid);

  benchPolyTreeOperation(
    'polytree - nested rectangles',
    ClipType.Difference,
    [[
      { x: 0, y: 0 },
      { x: 1000, y: 0 },
      { x: 1000, y: 1000 },
      { x: 0, y: 1000 },
    ]],
    [[
      { x: 200, y: 200 },
      { x: 800, y: 200 },
      { x: 800, y: 800 },
      { x: 200, y: 800 },
    ]],
  );

  benchPolyTreeOperation(
    'polytree - complex overlapping',
    ClipType.Intersection,
    overlappingPairs.medium.subject,
    overlappingPairs.medium.clip,
  );
});

describe('Instance Reuse', () => {
  const twoOverlapping: Paths64 = [
    [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }, { x: 0, y: 100 }],
    [{ x: 50, y: 50 }, { x: 150, y: 50 }, { x: 150, y: 150 }, { x: 50, y: 150 }],
  ];

  benchBooleanOperation('fresh instance - 2 overlapping rectangles', ClipType.Union, twoOverlapping);

  {
    const reusedClipper = new Clipper64();
    const reusedSolution: Paths64 = [];
    const klipSubject = toKlipPaths(twoOverlapping);
    const wasmSubject = toWasmPaths(twoOverlapping);
    const wasmClipper = new Wasm.Clipper64();
    const wasmSolution = newWasmPaths();

    describe('reused instance - 2 overlapping rectangles', () => {
      bench('clipper2-ts', () => {
        reusedClipper.clear();
        reusedClipper.addSubject(twoOverlapping);
        reusedSolution.length = 0;
        reusedClipper.execute(ClipType.Union, fillRule, reusedSolution);
      });

      bench('clipper2-wasm', () => {
        wasmClipper.Clear();
        wasmClipper.AddSubject(wasmSubject);
        wasmSolution.clear();
        wasmClipper.ExecutePath(wasmClipType(ClipType.Union), wasmFillRule(fillRule), wasmSolution);
      });

      bench('Klip', () => {
        Klip.booleanOp(ClipType.Union, klipSubject, null, fillRule);
      });
    });
  }

  const nestedSubject: Paths64 = [[
    { x: 0, y: 0 },
    { x: 1000, y: 0 },
    { x: 1000, y: 1000 },
    { x: 0, y: 1000 },
  ]];
  const nestedClip: Paths64 = [[
    { x: 200, y: 200 },
    { x: 800, y: 200 },
    { x: 800, y: 800 },
    { x: 200, y: 800 },
  ]];

  benchPolyTreeOperation('fresh polytree - nested rectangles', ClipType.Difference, nestedSubject, nestedClip);

  {
    const reusedClipper = new Clipper64();
    const reusedPolytree = new PolyTree64();
    const klipSubject = toKlipPaths(nestedSubject);
    const klipClip = toKlipPaths(nestedClip);
    const wasmSubject = toWasmPaths(nestedSubject);
    const wasmClip = toWasmPaths(nestedClip);
    const wasmClipper = new Wasm.Clipper64();
    const wasmPolytree = newWasmPolyTree();

    describe('reused polytree - nested rectangles', () => {
      bench('clipper2-ts', () => {
        reusedClipper.clear();
        reusedClipper.addSubject(nestedSubject);
        reusedClipper.addClip(nestedClip);
        reusedPolytree.clear();
        reusedClipper.execute(ClipType.Difference, fillRule, reusedPolytree);
      });

      bench('clipper2-wasm', () => {
        wasmClipper.Clear();
        wasmClipper.AddSubject(wasmSubject);
        wasmClipper.AddClip(wasmClip);
        wasmPolytree.clear();
        wasmClipper.ExecutePoly(wasmClipType(ClipType.Difference), wasmFillRule(fillRule), wasmPolytree);
      });

      bench('Klip', () => {
        // Klip's booleanOpPolyTree always returns a fresh tree (no reuse API).
        Klip.booleanOpPolyTree(ClipType.Difference, klipSubject, klipClip, fillRule);
      });
    });
  }
});

describe('Geo-scale Coordinates', () => {
  const scale = 360_000;
  const geoComplex: Path64 = testData.mediumComplex.map(p => ({
    x: Math.round(p.x * scale),
    y: Math.round(p.y * scale),
  }));
  const geoComplexShifted: Path64 = geoComplex.map(p => ({
    x: p.x + Math.round(200 * scale),
    y: p.y + Math.round(200 * scale),
  }));

  benchBooleanOperation('union - geo-scale complex polygon', ClipType.Union, [geoComplex]);
  benchBooleanOperation('intersection - geo-scale overlapping', ClipType.Intersection, [geoComplex], [geoComplexShifted]);
});

describe('Union Operations', () => {
  const rhinoPolysXY = loadRhinoPaths(1000.0);

  benchBooleanOperation('union - medium complex polygon', ClipType.Union, [testData.mediumComplex]);
  benchBooleanOperation('union - large complex polygon', ClipType.Union, [testData.largeComplex]);
  benchBooleanOperation('union - very large complex polygon (2000 vertices)', ClipType.Union, [testData.veryLargeComplex]);
  benchBooleanOperation('union - medium grid (25 rectangles)', ClipType.Union, testData.mediumGrid);
  benchBooleanOperation('union - large grid (100 rectangles)', ClipType.Union, testData.largeGrid);
  benchBooleanOperation('union - Rhino polysXY scaled 10e4', ClipType.Union, rhinoPolysXY);
});
