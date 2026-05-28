// Demonstrates a robustness regression introduced by commit b92b1d5 "no more Rounding".
//
// It transcribes Geo.crossProductSign from Src/Core.fs *exactly*, in both its
// pre-commit form and its current form, and finds an input on which they disagree.
//
// crossProductSign is THE orientation predicate of the Vatti sweep-line: it answers
// "is the turn pt1->pt2->pt3 left (+1), right (-1) or straight (0)?". The whole engine
// (AEL ordering, intersection detection, winding) assumes this answer is correct.
//
// ---------------------------------------------------------------------------
// BEFORE  (commit e854d32, Src/Core.fs): inputs were snapped to the integer grid
//         by jsRound, and the test was an EXACT comparison of the two products.
//         On integer coords the products are exact integers, so this is exact.
function crossOld(x1, y1, x2, y2, x3, y3) {
  const a = BigInt(x2 - x1), b = BigInt(y3 - y2);
  const c = BigInt(y2 - y1), d = BigInt(x3 - x2);
  const prod1 = a * b, prod2 = c * d;        // exact, no rounding
  if (prod1 > prod2) return 1;
  else if (prod1 < prod2) return -1;
  else return 0;
}

// AFTER   (commit b92b1d5, current Src/Core.fs): no rounding, and exact equality
//         is replaced by crossIsZero with a *relative* tolerance.
const crossCollinearRelTol = 1e-12;          // <- the literal from Src/Core.fs
function crossIsZero(prod1, prod2) {
  return Math.abs(prod1 - prod2) <= crossCollinearRelTol * (Math.abs(prod1) + Math.abs(prod2));
}
function crossNew(x1, y1, x2, y2, x3, y3) {
  const a = x2 - x1, b = y3 - y2;
  const c = y2 - y1, d = x3 - x2;
  const prod1 = a * b, prod2 = c * d;
  if (crossIsZero(prod1, prod2)) return 0;
  else if (prod1 > prod2) return 1;
  else return -1;
}
// ---------------------------------------------------------------------------

// Three points on the integer grid, at a CAD-realistic scale (~8 million units —
// well inside Clipper2's old int64 range and inside 2^53, so there is ZERO
// input-rounding error). The corner at pt2 is a genuine, non-degenerate left turn:
// the exact cross product is +1, not 0. This is a thin sliver — precisely the kind
// of near-collinear geometry that the integer engine was designed to handle exactly.
const pt1 = [0, 0];
const pt2 = [4_000_001, 4_000_000];
const pt3 = [8_000_003, 8_000_001];

const x1 = pt1[0], y1 = pt1[1], x2 = pt2[0], y2 = pt2[1], x3 = pt3[0], y3 = pt3[1];

// Exact, ground-truth cross product a*b - c*d at pt2 (BigInt = no float error):
const area2 = (BigInt(x2) - BigInt(x1)) * (BigInt(y3) - BigInt(y2))
            - (BigInt(y2) - BigInt(y1)) * (BigInt(x3) - BigInt(x2));

const before = crossOld(x1, y1, x2, y2, x3, y3);
const after  = crossNew(x1, y1, x2, y2, x3, y3);

console.log(`points: (${x1},${y1}) (${x2},${y2}) (${x3},${y3})`);
console.log(`exact cross product at pt2 (BigInt) : ${area2}   -> genuinely ${area2 === 0n ? "DEGENERATE" : "non-degenerate, a real corner"}`);
console.log(`crossProductSign BEFORE (int + exact)   : ${before}`);
console.log(`crossProductSign AFTER  (float + relTol): ${after}`);
console.log("");

// The "test": the orientation of a non-degenerate corner must be reported correctly.
const expected = area2 > 0n ? 1 : area2 < 0n ? -1 : 0;
let ok = true;
if (before !== expected) { console.log(`FAIL(before): expected ${expected}, got ${before}`); ok = false; }
else console.log(`PASS(before): orientation correctly reported as ${before}`);

if (after !== expected) {
  console.log(`FAIL(after) : expected ${expected} (left turn), got ${after} (engine now believes the corner is STRAIGHT/collinear)`);
} else {
  console.log(`PASS(after) : orientation correctly reported as ${after}`);
}

console.log("");
console.log(ok && after === expected
  ? "No regression."
  : "REGRESSION: a test that passes on the pre-commit (integer) engine fails on the current (float) engine.");

process.exit(after === expected ? 0 : 1);
