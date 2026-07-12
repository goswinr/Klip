# Exploratory scripts

Ad-hoc `dotnet fsi` / node scripts for exploring engine behavior, mostly from the
touching-union / tolerance investigation. **Not part of CI.**

All F# scripts reference the compiled library via a relative
`#r "../../../bin/Release/netstandard2.0/Klip.dll"`, so build it first:

```bash
dotnet build -c Release
```

## `console/` - run with plain `dotnet fsi` (or node)

| Script | Purpose |
| ------ | ------- |
| `issue1091.fsx` | Repro of [Clipper2 #1091](https://github.com/AngusJohnson/Clipper2/issues/1091): union of two triangles whose near-vertical edges are only 2e-8 apart. |
| `sweep-touching-union.fsx` | The big shift/scale/rotation/translation sweep over two touching rectangles (the fixture behind the `MergeVertexTolerance` / `CoordEqTolerance` work). Contains a commented-out Clipper2 comparison path. |
| `union-polysXY.fsx` | Self-union of the noisy `data/polysXY.json` dataset at 6 scales, compared against Clipper2 at matching precision. |
| `union-polysXY.mjs` | Same dataset through the Fable/JS bundle (`Test/TypeScript/_dist/Klip.mjs`; run `npm run build` there first). |

## `rhino/` - need Rhino 8 running (RhinoCommon + Rhino.Scripting, e.g. via Fesh)

| Script | Purpose |
| ------ | ------- |
| `interactive-union.fsx` | Select polylines in the active Rhino document, union them, draw the result and print the input as F# code for making repro fixtures. |
| `allTestsRh.fsx` | Runs all `Polygons.txt` fixture cases (from `Test/TypeScript/tests/test-data/`), compares area/count against expected and Clipper2, and can draw a selected case by caption. |
| `union-polysXY-Rh.fsx` | Draws the `data/polysXY.json` self-union next to Clipper2's result at several precisions. |
| `issue1083.fsx` | Repro of Clipper2 #1083: EvenOdd self-union of a ~310-vertex self-intersecting polygon. |
| `issue1085.fsx` | Repro of Clipper2 #1085: union producing a zero-width bridge that a second union splits. |
| `issue1091.fsx` | Draws the issue-1091 fixture and its union result. |
| `bridge-repro.fsx` | Two contours sharing a near-horizontal seam edge (the "bridge" case), swept over shift/rotation/scale. |
| `mergepoint-repro.fsx` | Single touching-union case where the merge point landed wrong, swept over shift magnitudes. |
| `rhinoToJson.fsx` | Regenerates `data/polysXY.json` / `data/polysXYOrig.json` (wobbled + original) from the polylines in `data/union.3dm`. |

## `data/`

`polysXY.json` / `polysXYOrig.json` (noisy and original polygon dataset exported from
`union.3dm` via `rhinoToJson.fsx`), `union.3dm`, `unionAllScale.3dm`, `poly.png`.

Historical note: earlier iterations of these sweeps (`unionAllScales*.fsx`, the
`Union-Touching` folder) were deleted in the reorganization that created this folder;
see git history if you need them. Their conclusions live in `CHANGELOG.md` and the
tolerance docs in `CLAUDE.md` / `Src/Engine.fs`.

Many scripts set the `[<Obsolete>]`-hidden individual tolerance properties on purpose
(that is what they were exploring) - the deprecation warnings are expected. The
supported knobs are `Clipper64.Tolerance` and `Clipper64.AngleTolerance`.
