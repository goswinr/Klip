# Bench bisect: `union - Rhino polysXY scaled 10e4`, main..dev (2026-09-18)

Case: `bench/clipping-operations.bench.ts > Union Operations > union - Rhino polysXY scaled 10e4 > Klip`
(10 unions of the `Test/Scripts/data/polysXY.json` fixture per bench op, `FAST_BENCH_OPTS`).

## Method

Every commit from `main` (9d41291) through `dev` (1966a15) was checked out in a detached scratch
worktree, `npm run build` was run (Fable → `_dist/Klip.mjs`), then
`npx vitest bench --run -t Rhino` was run **3 times**. The table reports the median of the three
per-run means in **ms per bench op**. The bench file, fixture, `Klip.fsproj` and toolchain are
identical between `main` and `dev`, so only `Src/*.fs` differs between rows.

`clipper2-wasm` and `clipper2-ts` run in the same bench group and are unchanged across rows; they
serve as a machine-noise control. Both stayed flat (wasm 0.41-0.46 ms, ts 1.07-1.16 ms).

The docs-only commit 4874ea3 moved by +3%, so treat any single step under ~5% as unconfirmed noise.

## Results

| #  | Commit  | Subject                                                        | Klip ms | vs main | vs prev | wasm ctrl |
|----|---------|----------------------------------------------------------------|--------:|--------:|--------:|----------:|
| 01 | 9d41291 | main: Give VitestFixtureBenchmarks a larger invocation budget   |   0.653 |     0%  |         |     0.425 |
| 02 | 972a0f8 | .                                                              |   0.638 |    -2%  |    -2%  |     0.423 |
| 03 | 4359559 | Fix float topology tolerances and consistent defaults          |   0.635 |    -3%  |    -1%  |     0.419 |
| 04 | 8182b09 | Keep exact boundary incidence at positive tolerances           |   0.681 |    +4%  |    +7%  |     0.428 |
| 05 | b87185f | Bound angular vertex cleanup by absolute distance              |   0.645 |    -1%  |    -5%  |     0.426 |
| 06 | fec5017 | Reject degenerate output rings and honor triangle culling      |   0.635 |    -3%  |    -1%  |     0.414 |
| 07 | e3c17a4 | Reject coordinate tolerance changes after input ingestion      |   0.642 |    -2%  |    +1%  |     0.408 |
| 08 | 24bf7d6 | Make tolerance round trips and angle ranges consistent         |   0.644 |    -1%  |     0%  |     0.411 |
| 09 | 63bd4a6 | Make snapping deterministic and stable at large coordinates    |   0.654 |     0%  |    +2%  |     0.415 |
| 10 | **ef12add** | **Stabilize angular predicates across coordinate scales**  | **0.740** | +13% | **+13%** |  0.410 |
| 11 | 6ea427b | Compute polygon areas relative to a local origin               |   0.746 |   +14%  |    +1%  |     0.427 |
| 12 | 68ac5a8 | Compare merge distances without squared underflow              |   0.779 |   +19%  |    +4%  |     0.420 |
| 13 | 9b56129 | Preserve near-top join margins at large offsets                |   0.774 |   +19%  |    -1%  |     0.420 |
| 14 | **8b3aae0** | **Validate finite input coordinates atomically**           | **0.864** | +32% | **+12%** |  0.420 |
| 15 | **23e3ff6** | **Use exact fallback for uncertain orientation and intersection determinants** | **1.022** | +57% | **+18%** | 0.445 |
| 16 | c4679d2 | Retain tiny positive angular tolerances without squaring       |   0.958 |   +47%  |    -6%  |     0.423 |
| 17 | **ae63596** | **Choose canonical representatives for closed near-duplicate chains** | **1.090** | +67% | **+14%** | 0.431 |
| 18 | 4874ea3 | Document tolerance fixes and compatibility changes (docs only) |   1.124 |   +72%  |    +3%  |     0.431 |
| 19 | 2511b8b | Make coordinate tolerance constructor-only                     |   1.125 |   +72%  |     0%  |     0.440 |
| 20 | 99b3b49 | Support optional constructor angle tolerance and explain its lifecycle | 1.167 | +79% | +4%  |     0.457 |
| 21 | 9aedbeb | Revert "Use exact fallback ..."                                | build fails (stale `colinTolSqrd` in Core.fs) | | | |
| 22 | 1966a15 | dev: Fix stale colinTolSqrd reference left by revert           |   0.968 | **+48%** |  -17%  |     0.416 |

Net: **dev is ~48% slower than main** on this case (0.653 → 0.968 ms). Commit 15 was already
reverted (rows 21/22 recover its 18%). Three regressions remain in dev:

| Commit  | Step  | Hot spot                                                                                  |
|---------|------:|-------------------------------------------------------------------------------------------|
| ef12add | +13%  | `Geo.crossIsZero` / `Geo.dotProductSign`: per-call max/abs, 4 divisions, 1-2 sqrt; `dotProductSign` lost `inline`. Called from `checkJoinLeft/Right` and the output-ring cleanup loop. |
| 8b3aae0 | +12%  | `Clipper64.AddPaths`: `for coordinate in path.XYs` (enumerator over `ResizeArray` in Fable output) plus `Double.IsNaN`/`IsInfinity` per coordinate, over every input coordinate on every call. |
| ae63596 | +14%  | `Geo.closedPathRepresentatives` pre-scan per closed path (`GetX`/`GetY` method calls, `%` per vertex, closure) plus a per-vertex `pointIndex` closure + option match in the vertex-building loop, even when the result is `None`. |

The remaining ~8% is spread over 68ac5a8 (+4%), 4874ea3 (+3%, docs only → noise) and 99b3b49 (+4%);
none is individually above the noise floor. Re-measure after fixing the three above.

## Plan (see the session notes / commit messages for status)

1. **ef12add rewrite: filtered fast path.** Keep main's one-compare squared form
   `cross² <= tol² * |U|²|W|²` when the operands are safely inside the normal float range, and only
   fall back to the per-vector normalized form when `tol² * scaleSq` is zero/denormal or `scaleSq`
   is near overflow (or `tol = 0`, exact mode). Same for `dotProductSign`: take the sign of the raw
   dot product when its magnitude is normal and finite, normalize only otherwise. Restore `inline`.
   The extreme-scale tests (1e-200 .. 1e200) keep passing through the fallback.
2. **8b3aae0 rewrite: indexed loop.** Replace `for coordinate in path.XYs` with a `Rarr.len` /
   `Rarr.getIdx` loop and a single `coordinate - coordinate <> 0.` (true for NaN and ±Infinity)
   test, preserving the validate-everything-before-mutating contract.
3. **ae63596 rewrite: free `None` path.** Do the adjacent-near-duplicate pre-scan as an indexed loop
   over the flat buffer (wrap pair handled once, no `%`, no closure), and keep two vertex-building
   loops: main's original loop when there are no near duplicates, the index-indirected one only when
   there are. Optionally compute the per-path "has near duplicates" flag inside the validation pass
   from step 2 so the input is scanned once.
4. **Changelog cleanup.** The `[Unreleased]` entry still describes the reverted exact-fallback
   predicates (commit 23e3ff6); trim it to what survived (local-origin area accumulation).
5. **Verify.** `dotnet build`, both F# test projects, `npm run build && npm test`, then re-run this
   bisect script (or at least main vs dev head, 3 reps, longer `time`) and require dev within ~5% of
   main. Also run the BenchmarkDotNet suite once to confirm the .NET side.

Raw per-run JSON/logs from this bisect live only in the session scratchpad; this table is the record.
