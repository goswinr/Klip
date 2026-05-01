// Mirrors clipper2-ts/bench/clipping-operations.bench.ts but with each
// operation benched twice — once against the published `clipper2-ts` npm
// package, once against Klip's bundled `_dist/Klip.mjs` — so the two
// implementations show up side by side in vitest's bench output.
//
// Klip inputs are pre-converted to `{xs, ys, zs}` (parallel-buffer Path64) once
// per top-level scope; the conversion cost is intentionally excluded from the
// timed regions, mirroring how clipper2-ts's bench excludes input setup.
//
// Klip skips: open subjects, polytree-area assertions, anything outside the
// `booleanOp` / `booleanOpWithPolyTree` / `polyTreeToPaths64` surface.

import { bench, describe } from 'vitest';
import {
  Clipper64,
  PolyTree64,
  Clipper,
  ClipType,
  FillRule,
  type Paths64,
  type Path64
} from 'clipper2-ts';
import { testData, overlappingPairs } from './test-data';
import {
  toLipPath,
  toLipPaths,
  createLipPolyTree,
  booleanOp as Lip_booleanOp,
  booleanOpWithPolyTree as Lip_booleanOpWithPolyTree,
  intersect as Lip_intersect,
  union as Lip_union,
  unionSelf as Lip_unionSelf,
  difference as Lip_difference
} from './lip-helpers';

// Klip-side inputs (converted once).
const lip_mediumComplex   = toLipPath(testData.mediumComplex);
const lip_largeComplex    = toLipPath(testData.largeComplex);
const lip_veryLargeComplex= toLipPath(testData.veryLargeComplex);
const lip_mediumGrid      = toLipPaths(testData.mediumGrid);
const lip_largeGrid       = toLipPaths(testData.largeGrid);
const lip_pair_medium_subject = toLipPaths(overlappingPairs.medium.subject);
const lip_pair_medium_clip    = toLipPaths(overlappingPairs.medium.clip);
const lip_pair_large_subject  = toLipPaths(overlappingPairs.large.subject);
const lip_pair_large_clip     = toLipPaths(overlappingPairs.large.clip);
const lip_pair_grid_subject   = toLipPaths(overlappingPairs.grid.subject);
const lip_pair_grid_clip      = toLipPaths(overlappingPairs.grid.clip);

describe('union — medium complex polygon', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject([testData.mediumComplex]);
    const solution: Paths64 = [];
    c.execute(ClipType.Union, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Union, [lip_mediumComplex], [], FillRule.NonZero);
  });
});

describe('union — large complex polygon', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject([testData.largeComplex]);
    const solution: Paths64 = [];
    c.execute(ClipType.Union, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Union, [lip_largeComplex], [], FillRule.NonZero);
  });
});

describe('union — very large complex polygon (2000 vertices)', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject([testData.veryLargeComplex]);
    const solution: Paths64 = [];
    c.execute(ClipType.Union, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Union, [lip_veryLargeComplex], [], FillRule.NonZero);
  });
});

describe('union — medium grid (25 rectangles)', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(testData.mediumGrid);
    const solution: Paths64 = [];
    c.execute(ClipType.Union, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Union, lip_mediumGrid, [], FillRule.NonZero);
  });
});

describe('union — large grid (100 rectangles)', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(testData.largeGrid);
    const solution: Paths64 = [];
    c.execute(ClipType.Union, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Union, lip_largeGrid, [], FillRule.NonZero);
  });
});

describe('intersection — medium overlapping polygons', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(overlappingPairs.medium.subject);
    c.addClip(overlappingPairs.medium.clip);
    const solution: Paths64 = [];
    c.execute(ClipType.Intersection, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Intersection, lip_pair_medium_subject, lip_pair_medium_clip, FillRule.NonZero);
  });
});

describe('intersection — large overlapping polygons', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(overlappingPairs.large.subject);
    c.addClip(overlappingPairs.large.clip);
    const solution: Paths64 = [];
    c.execute(ClipType.Intersection, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Intersection, lip_pair_large_subject, lip_pair_large_clip, FillRule.NonZero);
  });
});

describe('intersection — grid with rectangle', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(overlappingPairs.grid.subject);
    c.addClip(overlappingPairs.grid.clip);
    const solution: Paths64 = [];
    c.execute(ClipType.Intersection, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Intersection, lip_pair_grid_subject, lip_pair_grid_clip, FillRule.NonZero);
  });
});

describe('difference — medium overlapping polygons', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(overlappingPairs.medium.subject);
    c.addClip(overlappingPairs.medium.clip);
    const solution: Paths64 = [];
    c.execute(ClipType.Difference, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Difference, lip_pair_medium_subject, lip_pair_medium_clip, FillRule.NonZero);
  });
});

describe('difference — large overlapping polygons', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(overlappingPairs.large.subject);
    c.addClip(overlappingPairs.large.clip);
    const solution: Paths64 = [];
    c.execute(ClipType.Difference, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Difference, lip_pair_large_subject, lip_pair_large_clip, FillRule.NonZero);
  });
});

describe('xor — medium overlapping polygons', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(overlappingPairs.medium.subject);
    c.addClip(overlappingPairs.medium.clip);
    const solution: Paths64 = [];
    c.execute(ClipType.Xor, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Xor, lip_pair_medium_subject, lip_pair_medium_clip, FillRule.NonZero);
  });
});

describe('xor — large overlapping polygons', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(overlappingPairs.large.subject);
    c.addClip(overlappingPairs.large.clip);
    const solution: Paths64 = [];
    c.execute(ClipType.Xor, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Xor, lip_pair_large_subject, lip_pair_large_clip, FillRule.NonZero);
  });
});

describe('10 union operations on medium polygons', () => {
  bench('clipper2-ts', () => {
    for (let i = 0; i < 10; i++) {
      const c = new Clipper64();
      c.addSubject([testData.mediumComplex]);
      const solution: Paths64 = [];
      c.execute(ClipType.Union, FillRule.NonZero, solution);
    }
  });
  bench('Klip', () => {
    for (let i = 0; i < 10; i++) {
      Lip_booleanOp(ClipType.Union, [lip_mediumComplex], [], FillRule.NonZero);
    }
  });
});

describe('10 intersection operations on medium polygons', () => {
  bench('clipper2-ts', () => {
    for (let i = 0; i < 10; i++) {
      const c = new Clipper64();
      c.addSubject(overlappingPairs.medium.subject);
      c.addClip(overlappingPairs.medium.clip);
      const solution: Paths64 = [];
      c.execute(ClipType.Intersection, FillRule.NonZero, solution);
    }
  });
  bench('Klip', () => {
    for (let i = 0; i < 10; i++) {
      Lip_booleanOp(ClipType.Intersection, lip_pair_medium_subject, lip_pair_medium_clip, FillRule.NonZero);
    }
  });
});

describe('convenience union — medium grid', () => {
  bench('clipper2-ts (Clipper.union)', () => {
    Clipper.union(testData.mediumGrid, FillRule.NonZero);
  });
  bench('Klip (unionSelf)', () => {
    Lip_unionSelf(lip_mediumGrid, FillRule.NonZero);
  });
});

describe('convenience intersect — medium overlapping', () => {
  bench('clipper2-ts (Clipper.intersect)', () => {
    Clipper.intersect(
      overlappingPairs.medium.subject,
      overlappingPairs.medium.clip,
      FillRule.NonZero
    );
  });
  bench('Klip (intersect)', () => {
    Lip_intersect(lip_pair_medium_subject, lip_pair_medium_clip, FillRule.NonZero);
  });
});

describe('convenience difference — medium overlapping', () => {
  bench('clipper2-ts (Clipper.difference)', () => {
    Clipper.difference(
      overlappingPairs.medium.subject,
      overlappingPairs.medium.clip,
      FillRule.NonZero
    );
  });
  bench('Klip (difference)', () => {
    Lip_difference(lip_pair_medium_subject, lip_pair_medium_clip, FillRule.NonZero);
  });
});

// === Simple Union Operations ============================================
const simpleRects: Paths64 = [
  [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }, { x: 0, y: 100 }],
  [{ x: 150, y: 150 }, { x: 250, y: 150 }, { x: 250, y: 250 }, { x: 150, y: 250 }],
  [{ x: 300, y: 0 }, { x: 400, y: 0 }, { x: 400, y: 100 }, { x: 300, y: 100 }]
];
const lip_simpleRects = toLipPaths(simpleRects);

const twoOverlapping: Paths64 = [
  [{ x: 0, y: 0 }, { x: 100, y: 0 }, { x: 100, y: 100 }, { x: 0, y: 100 }],
  [{ x: 50, y: 50 }, { x: 150, y: 50 }, { x: 150, y: 150 }, { x: 50, y: 150 }]
];
const lip_twoOverlapping = toLipPaths(twoOverlapping);

const simplePath: Path64 = [];
for (let i = 0; i < 8; i++) {
  const angle = (i / 8) * Math.PI * 2;
  simplePath.push({
    x: Math.round(Math.cos(angle) * 50 + 100),
    y: Math.round(Math.sin(angle) * 50 + 100)
  });
}
const fourCircles: Paths64 = [
  simplePath,
  simplePath.map((p) => ({ x: p.x + 200, y: p.y })),
  simplePath.map((p) => ({ x: p.x, y: p.y + 200 })),
  simplePath.map((p) => ({ x: p.x + 200, y: p.y + 200 }))
];
const lip_fourCircles = toLipPaths(fourCircles);

describe('union — 3 non-overlapping rectangles', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(simpleRects);
    const solution: Paths64 = [];
    c.execute(ClipType.Union, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Union, lip_simpleRects, [], FillRule.NonZero);
  });
});

describe('union — 2 overlapping rectangles', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(twoOverlapping);
    const solution: Paths64 = [];
    c.execute(ClipType.Union, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Union, lip_twoOverlapping, [], FillRule.NonZero);
  });
});

describe('union — 4 simple circles (no self-intersection)', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(fourCircles);
    const solution: Paths64 = [];
    c.execute(ClipType.Union, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Union, lip_fourCircles, [], FillRule.NonZero);
  });
});

// === PolyTree Operations ================================================
const ptDiffOuter: Paths64 = [
  [{ x: 0, y: 0 }, { x: 1000, y: 0 }, { x: 1000, y: 1000 }, { x: 0, y: 1000 }]
];
const ptDiffInner: Paths64 = [
  [{ x: 200, y: 200 }, { x: 800, y: 200 }, { x: 800, y: 800 }, { x: 200, y: 800 }]
];
const lip_ptDiffOuter = toLipPaths(ptDiffOuter);
const lip_ptDiffInner = toLipPaths(ptDiffInner);

describe('polytree — medium grid union', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(testData.mediumGrid);
    const polytree = new PolyTree64();
    c.execute(ClipType.Union, FillRule.NonZero, polytree);
  });
  bench('Klip', () => {
    const polytree = createLipPolyTree();
    Lip_booleanOpWithPolyTree(ClipType.Union, lip_mediumGrid, [], polytree, FillRule.NonZero);
  });
});

describe('polytree — nested rectangles (difference)', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(ptDiffOuter);
    c.addClip(ptDiffInner);
    const polytree = new PolyTree64();
    c.execute(ClipType.Difference, FillRule.NonZero, polytree);
  });
  bench('Klip', () => {
    const polytree = createLipPolyTree();
    Lip_booleanOpWithPolyTree(ClipType.Difference, lip_ptDiffOuter, lip_ptDiffInner, polytree, FillRule.NonZero);
  });
});

describe('polytree — complex overlapping (intersection)', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject(overlappingPairs.medium.subject);
    c.addClip(overlappingPairs.medium.clip);
    const polytree = new PolyTree64();
    c.execute(ClipType.Intersection, FillRule.NonZero, polytree);
  });
  bench('Klip', () => {
    const polytree = createLipPolyTree();
    Lip_booleanOpWithPolyTree(ClipType.Intersection, lip_pair_medium_subject, lip_pair_medium_clip, polytree, FillRule.NonZero);
  });
});

// === Geo-scale Coordinates ==============================================
const scale = 360_000;
const geoComplex: Path64 = testData.mediumComplex.map(p => ({
  x: Math.round(p.x * scale),
  y: Math.round(p.y * scale)
}));
const geoComplexShifted: Path64 = geoComplex.map(p => ({
  x: p.x + Math.round(200 * scale),
  y: p.y + Math.round(200 * scale)
}));
const lip_geoComplex        = toLipPath(geoComplex);
const lip_geoComplexShifted = toLipPath(geoComplexShifted);

describe('union — geo-scale complex polygon', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject([geoComplex]);
    const solution: Paths64 = [];
    c.execute(ClipType.Union, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Union, [lip_geoComplex], [], FillRule.NonZero);
  });
});

describe('intersection — geo-scale overlapping', () => {
  bench('clipper2-ts', () => {
    const c = new Clipper64();
    c.addSubject([geoComplex]);
    c.addClip([geoComplexShifted]);
    const solution: Paths64 = [];
    c.execute(ClipType.Intersection, FillRule.NonZero, solution);
  });
  bench('Klip', () => {
    Lip_booleanOp(ClipType.Intersection, [lip_geoComplex], [lip_geoComplexShifted], FillRule.NonZero);
  });
});
