namespace Klip.Tests

// the individual tolerance properties are [<Obsolete>]-hidden but exercised here on purpose
#nowarn "44"

open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.Tests.Helpers



type Pt(x:float,y:float) =
    member _.X = x
    member _.Y = y


[<TestClass>]
type BooleanTests () =

    let square (x: float) (y: float) (size: float) =
        path [| x;       y
                x+size;  y
                x+size;  y+size
                x;       y+size |]


    let sharedVertexTouchingPolygons () =
        let subj = paths [ path [| 9.0;  35.0
                                   9.0;  37.0
                                   7.0;  37.00000000000001
                                   7.0;  34.0
                                   14.0; 34.0
                                   14.0; 37.00000000000001
                                   12.0; 37.0
                                   12.0; 35.0 |] ]
        let clip = paths [ path [| 7.0;  37.00000000000001
                                   8.0;  37.0
                                   8.0;  38.0
                                   7.0;  38.0 |] ]
        subj, clip


    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsSharedVertexGivesOnePolygonRot90 () =
        // The 90-degree-rotated twin of UnionTwoTouchingPolygonsSharedVertexGivesOnePolygon:
        // the noisy shared seam is now near-VERTICAL with a 1e-5 X deviation (-37 vs -37.00001).
        // Horizontal touching seams have a dedicated join pass (convertHorzSegsToJoins), but a
        // near-vertical seam merges only via the adjacent-edge join (checkJoinLeft/Right), whose
        // perpendicular-distance tolerance is MergeVertexTolerance. Its default equals coordEqTol
        // (1e-5) -- a hair too small for a 1e-5 gap -- so raise it to let the two contours merge.
        let subj =
            ResizeArray [
                 [| Pt(-35, 9.000000000000002); Pt(-37, 9.000000000000002); Pt(-37.00001, 7.000000000000003); Pt(-34, 7.000000000000002); Pt(-34, 14.000000000000002); Pt(-37.00001, 14.000000000000002); Pt(-37, 12.000000000000002); Pt(-35, 12.000000000000002) |]
                 [| Pt(-37.00001, 7.000000000000003); Pt(-37, 8.000000000000002); Pt(-38, 8.000000000000002); Pt(-38, 7.000000000000003) |]
            ]
            |>  Paths64.createFromXYMembers

        let c = Clipper64<unit>()
        c.MergeVertexTolerance <- 1e-4
        c.AddSubject(subj)
        let solution,_ = c.Execute(ClipType.Union, FillRule.NonZero)
        Assert.AreEqual(1, solution.Count, "expected a single merged polygon")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsSharedVertexGivesOnePolygon () =
        // From Test/Scripts/rhino/interactive-union.fsx (formerly unionManyRh.fsx): an arch with an open notch in its top edge,
        // plus a small square sitting on the left tine. The y=37.0 / y=37.00000000000001
        // float noise (~1e-14 apart) used to land on two distinct exact scanlines, sealing
        // the open notch into a phantom hole (2 contours). The tolerance-based isHorizontal
        // (HorizontalAngleTolerance) absorbs the noise, so this unions into one polygon
        // without any Snap pre-pass. (closing duplicate points from the source polylines are dropped)
        let subj, clip = sharedVertexTouchingPolygons()
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged polygon")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsSharedVertexGivesOnePolygon2 () =
        // Clipper64 never snaps; driving it directly (without the Snap pre-pass) reproduces
        // the old unsnapped regression of two contours.
        let subj, clip = sharedVertexTouchingPolygons()
        // Snap.xAndYMany 0.000001 [subj; clip] // TODO should alo pass without snapping !!
        let c = Clipper64<unit>()
        c.AddSubject(subj)
        c.AddClip(clip)
        let solution,_ = c.Execute(ClipType.Union, FillRule.NonZero)
        Assert.AreEqual(1, solution.Count, "expected a single merged polygon")

    [<TestMethod>]
    member _.UnionSelfNearHorizontalContinuationOfHorizontalEdge () =
        // Regression: a bound that runs exactly horizontal (100,37)->(60,37) and then continues
        // near-horizontal (60,37)->(20,36.999999999) (slope ratio 2.5e-11, well within the default
        // HorizontalAngleTolerance of 1e-5). doHorizontal's loop exit tests the next vertex Y with
        // EXACT equality, so it exits and hands the near-flat continuation to updateEdgeIntoAEL,
        // which classifies it horizontal (no scanline for its top). Without re-queuing it into the
        // horizontal list (as doTopOfScanbeam does), the edge is orphaned in the AEL with dx = +/-inf,
        // its curX evaluates to -infinity at the next scanbeam, and the ring closes early - the
        // left column (x 0..20, y 0..37) goes missing from the union result.
        let subj = paths [ path [| 0.0;   0.0
                                   20.0;  0.0
                                   20.0;  36.999999999
                                   60.0;  37.0
                                   100.0; 37.0
                                   100.0; 100.0
                                   0.0;   100.0 |] ]
        let solution = Klipper.unionSelf subj
        Assert.AreEqual(1, solution.Count, "expected a single polygon")
        // full ring area: 100*63 above y=37 plus 20*37 left column = 7040 (within 1e-9-noise)
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 7040.0) < 0.5,
            sprintf "expected union area ~7040, got %f" area)

    [<TestMethod; Timeout(10000)>] // a regression hangs the sweep, so fail on timeout instead
    member _.UnionSelfNearHorizontalBottomWithExactMidpointMinimumTerminates () =
        // Regression for the doHorizontal scanbeam ping-pong (path 70 of Test/Scripts/data/polysXY.json,
        // minimized): a rectangle whose bottom edge E->A->B is near-horizontal (within
        // HorizontalAngleTolerance) but carries ~1e-6 Y-noise with the midpoint A as the EXACT
        // local maximum of the sweep, so the two bounds reach the shared near-flat run on
        // different exact scanlines (B.y, then E.y). When the left bound's horizontal ended at A,
        // its maxima-pair edge was not in the AEL yet (Clipper2's integer coordinates guarantee
        // it is); doHorizontal hunted for it past the AEL end, then advanced the bound PAST the
        // maximum and back up the opposite side of the contour, re-inserting the already-swept
        // top scanline - an endless ping-pong between the two scanlines. The fix detects the
        // absent pair and parks the edge in the AEL at its top until the opposite bound arrives
        // and claims it as its maxima pair.
        let subj = paths [ path [| 17.823630756706656; -3.1776745347575104    // A: exact bottom-most
                                   14.000866896598076; -3.177673366258442     // B
                                   14.000867801900924; -1.4574267995238073    // C
                                   20.834059175905757; -1.4574258437039758    // D: exact top-most
                                   20.834058932959618; -3.1776739283677022 |] ] // E
        let expectedArea = abs subj.[0].SignedArea
        let solution = Klipper.unionSelfChecked subj
        Assert.AreEqual(1, solution.Count, "expected the polygon back as a single contour")
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - expectedArea) < 1e-3,
            sprintf "expected union area ~%f, got %f" expectedArea area)

    [<TestMethod; Timeout(10000)>] // a regression hangs the sweep, so fail on timeout instead
    member _.UnionSelfNearHorizontalExtremaDisjointRingsStaySeparate () =
        // Companion to UnionSelfNearHorizontalBottomWithExactMidpointMinimumTerminates (and the
        // vitest "disjoint near-horizontal extrema" case): two disjoint rings from
        // Test/Scripts/data/polysXY.json whose near-flat runs all carry sub-tolerance Y-noise, so both
        // rings exercise the parked-edge maxima handling within one sweep. They must come back
        // as two separate contours with their input areas intact (no merge, no loss).
        let subj = paths [ path [| 31.68114899807879;  -3.1776702435233077
                                   31.681147991813326; -5.232407663485913
                                   29.05299627590947;  -5.23240949946618
                                   29.052995652930253; -3.177669208459603 |]
                           path [| 17.823630756706656; -3.1776745347575104
                                   14.000866896598076; -3.177673366258442
                                   14.000867801900924; -1.4574267995238073
                                   20.834059175905757; -1.4574258437039758
                                   20.834058932959618; -3.1776739283677022 |] ]
        let expectedArea = abs subj.[0].SignedArea + abs subj.[1].SignedArea
        let solution = Klipper.unionSelfChecked subj
        Assert.AreEqual(2, solution.Count, "expected the two disjoint polygons back unmerged")
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - expectedArea) < 1e-3,
            sprintf "expected union area ~%f, got %f" expectedArea area)

    [<TestMethod>]
    member _.UnionTwoOverlappingSquaresGivesOnePolygon () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let solution = Klipper.union clip subj |> roundPaths
        Assert.AreEqual(1, solution.Count, "expected one merged polygon")
        // two 10x10 squares overlapping by 5x5 -> 100 + 100 - 25 = 175
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 175.0) < 0.5,
            sprintf "expected union area ~175, got %f" area)

    [<TestMethod>]
    member _.IntersectTwoOverlappingSquaresGives5x5Square () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let solution = Klipper.intersect clip subj |> roundPaths
        Assert.AreEqual(1, solution.Count)
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 25.0) < 0.5,
            sprintf "expected intersection area 25, got %f" area)

    [<TestMethod>]
    member _.DifferenceTwoOverlappingSquaresArea () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let solution = Klipper.difference clip subj |> roundPaths
        Assert.AreEqual(1, solution.Count)
        // 100 - 25 = 75
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 75.0) < 0.5,
            sprintf "expected difference area 75, got %f" area)

    [<TestMethod>]
    member _.XorTwoOverlappingSquaresArea () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let solution = Klipper.xor clip subj |> roundPaths
        // expected: two L-shapes (or one polygon with hole) - total area = 75 + 75 = 150
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 150.0) < 0.5,
            sprintf "expected xor area 150, got %f" area)

    [<TestMethod>]
    member _.UnionDisjointSquaresStaysSeparate () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 100.0 100.0 10.0 ]
        let solution = Klipper.union clip subj |> roundPaths
        Assert.AreEqual(2, solution.Count, "disjoint squares should remain separate")
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 200.0) < 0.5)

    [<TestMethod>]
    member _.UnionSelfResolvesSelfIntersectingPath () =
        // bow-tie: two triangles sharing a self-intersection point in the middle
        let subj = paths [ path [| 0.0;0.0; 10.0;10.0; 10.0;0.0; 0.0;10.0 |] ]
        let solution = Klipper.unionSelf subj |> roundPaths
        Assert.IsTrue(solution.Count >= 1, "expected at least one polygon")
        let area = totalAbsArea solution
        // each triangle is 25, total 50
        Assert.IsTrue(abs (area - 50.0) < 0.5,
            sprintf "expected resolved bow-tie area 50, got %f" area)

    [<TestMethod>]
    member _.EmptySubjectUnionReturnsClip () =
        let subj = Paths64<unit>()
        let clip = paths [ square 0.0 0.0 10.0 ]
        let solution = Klipper.union clip subj |> roundPaths
        // empty subject + non-empty clip union = clip
        Assert.AreEqual(1, solution.Count)
        Assert.IsTrue(abs (totalAbsArea solution - 100.0) < 0.5)

    [<TestMethod>]
    member _.EmptySubjectIntersectIsEmpty () =
        let subj = Paths64<unit>()
        let clip = paths [ square 0.0 0.0 10.0 ]
        let solution = Klipper.intersect clip subj
        Assert.AreEqual(0, solution.Count)

    // [<TestMethod>]
    // member _.NullSubjectGivesEmptySolution () =
    //     let clip = paths [ square 0.0 0.0 10.0 ]
    //     let solution = Klipper.union clip null
    //     Assert.AreEqual(0, solution.Count)

    [<TestMethod>]
    member _.Clipper64ExecuteSucceedsAndProducesUnion () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let c = Clipper64<unit>()
        c.AddSubject(subj)
        c.AddClip(clip)
        let solution,_ =  c.Execute(ClipType.Union, FillRule.NonZero)
        Assert.AreEqual(1, solution.Count)

    [<TestMethod>]
    member _.Clipper64ExecuteWithoutPathsReturnsEmpty () =
        let c = Clipper64<unit>()
        let solution, solutionOpen = c.Execute(ClipType.Union, FillRule.NonZero)
        Assert.AreEqual(0, solution.Count)
        Assert.IsNull(solutionOpen)

    [<TestMethod>]
    member _.Clipper64RejectsOpenClipPaths () =
        let clip = paths [ square 0.0 0.0 10.0 ]
        let c = Clipper64<unit>()
        let mutable threw = false

        try
            c.AddPaths(clip, PathType.Clip, true)
        with
        | :? System.ArgumentException as ex ->
            threw <- true
            Assert.AreEqual("isOpen", ex.ParamName)

        Assert.IsTrue(threw, "clip paths must be closed")

    [<TestMethod>]
    member _.Path64EnableZRejectsPathThatAlreadyHasZValues () =
        let pathWithZ = Path64.createEmptyZ<unit>()
        let mutable threw = false

        try
            Path64.enableZ<int> pathWithZ |> ignore
        with
        | :? System.InvalidOperationException -> threw <- true

        Assert.IsTrue(threw, "enableZ should reject paths that already carry Z values")

    [<TestMethod>]
    member _.Paths64EnableZWithRejectsMismatchedPathCount () =
        let ps = paths [ square 0.0 0.0 10.0 ]
        let zs = ResizeArray<ResizeArray<int>>()
        let mutable threw = false

        try
            Paths64.enableZWith zs ps |> ignore
        with
        | :? System.ArgumentException -> threw <- true

        Assert.IsTrue(threw, "enableZWith should reject Z collections that do not match the path count")

    [<TestMethod>]
    member _.EmptySubjectUnionWithoutClipReturnsEmpty () =
        let solution = Klipper.booleanOp(ClipType.Union, Paths64<unit>(), null, FillRule.NonZero)
        Assert.AreEqual(0, solution.Count)

    [<TestMethod>]
    member _.Clipper64ExecuteCanBeCalledTwiceWithEvenOddFill () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let c = Clipper64<unit>()
        c.AddSubject(subj)

        let first,_ = c.Execute(ClipType.Union, FillRule.EvenOdd)
        let second,_ = c.Execute(ClipType.Union, FillRule.EvenOdd)

        Assert.AreEqual(1, first.Count, "expected first execute to produce one polygon")
        Assert.AreEqual(1, second.Count, "expected second execute to produce the same polygon")
        Assert.IsTrue(abs (totalAbsArea second - 100.0) < 0.5)


    [<TestMethod>]
    member _.Clipper64MergeVertexToleranceCanBeCustomized () =
        let c = Clipper64<unit>()
        c.MergeVertexTolerance <- 1e-8
        Assert.AreEqual(1e-8, c.MergeVertexTolerance)


    [<TestMethod>]
    member _.Clipper64ColinearityToleranceCanBeCustomized () =
        let c = Clipper64<unit>()
        c.ColinearityTolerance <- 5e-9
        Assert.AreEqual(5e-9, c.ColinearityTolerance)

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsGivesEightPointPath () =
        let subj = paths [ path [| 3.0; 4.0
                                   5.0; 4.0
                                   5.0; 6.262690989118975
                                   3.0; 6.262690989118975 |] ]
        let clip = paths [ path [| 5.0; 5.0
                                   7.0; 5.0
                                   7.0; 7.0
                                   4.0; 7.0
                                   4.0; 6.262690989118976
                                   5.0; 6.262690989118975 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsNegativeGivesEightPointPath () =
        let subj = paths [ path [| -3.0000000000000004; -3.9999999999999996
                                   -5.000000000000001;  -3.9999999999999996
                                   -5.000000000000001;  -6.262690989118974
                                   -3.000000000000001;  -6.262690989118975 |] ]
        let clip = paths [ path [| -5.000000000000001;  -4.999999999999999
                                   -7.000000000000001;  -4.999999999999999
                                   -7.000000000000001;  -6.999999999999999
                                   -4.000000000000001;  -6.999999999999999
                                   -4.000000000000001;  -6.262690989118975
                                   -5.000000000000001;  -6.262690989118974 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsLargeGivesEightPointPath () =
        let subj = paths [ path [| -40000.0;            30000.000000000004
                                   -40000.0;            50000.0
                                   -62626.90989118975;  50000.00000000001
                                   -62626.90989118975;  30000.000000000004 |] ]
        let clip = paths [ path [| -50000.0;            50000.0
                                   -49999.99999999999;  70000.0
                                   -70000.0;            70000.0
                                   -70000.0;            40000.00000000001
                                   -62626.90989118976;  40000.00000000001
                                   -62626.90989118975;  50000.00000000001 |] ]
        // At ~5e4 scale the merged top edge leaves an exactly-colinear zero-area U-turn
        // spike that the default (snap-on, tight-angle) settings keep as a 9th vertex.
        // The fixed absolute join constants are tuned for ~unit..1e4 scale; at this
        // magnitude no single default removes it. Per-case tuning that does: disable the
        // sub-tolerance input snap and relax the colinearity (angle) threshold so the
        // colinear spike is cleaned. See union-touching scale notes in README.
        // ColinearityTolerance is process-wide, so save and restore it for test isolation.
        let c = Clipper64<unit>()
        let prevColl = c.ColinearityTolerance
        try
            // Clipper64 no longer snaps; coordinates are used as given (the Snap module is opt-in).
            c.ColinearityTolerance <- 0.1
            c.AddSubject(subj)
            c.AddClip(clip)
            let solution,_ = c.Execute(ClipType.Union, FillRule.NonZero)
            Assert.AreEqual(1, solution.Count, "expected a single merged path")
            Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")
        finally
            c.ColinearityTolerance <- prevColl

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsLargeNoClosingVertexGivesEightPointPath () =
        // Same large-scale touching polygons as above, but driven through the default
        // Klipper.union API with NO repeated first==last closing vertex. Currently the
        // colinear U-turn spike on the merged top edge survives as a 9th vertex -> FAILS.
        let subj = paths [ path [| -40000.0;            30000.000000000004
                                   -40000.0;            50000.0
                                   -62626.90989118975;  50000.00000000001
                                   -62626.90989118975;  30000.000000000004 |] ]
        let clip = paths [ path [| -50000.0;            50000.0
                                   -49999.99999999999;  70000.0
                                   -70000.0;            70000.0
                                   -70000.0;            40000.00000000001
                                   -62626.90989118976;  40000.00000000001
                                   -62626.90989118975;  50000.00000000001 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionHalfSnappedTouchingPolygonsRemovesHorizontalSpikeWithoutSnap () =
        // From the removed touching-union script test-on-one_large.fsx (see git history
        // of Test/Union-Touching/, now Test/Scripts/). A 1e-14 deviation at the
        // shared top-left vertex leaves a horizontal U-turn in the materialized output:
        // (-40,50) -> (-62,50) -> (-50,50). Clipper64 should remove that spike without
        // requiring the Snap pre-pass.
        let subject = paths [ path [| -50.0; 50.0
                                      -40.0; 70.0
                                      -70.0; 70.0
                                      -70.0; 40.0
                                      -62.0; 40.0
                                      -62.0; 50.00000000000001 |]
                              path [| -40.0; 30.0
                                      -40.0; 50.0
                                      -62.0; 50.0
                                      -62.0; 30.0 |] ]
        let c = Clipper64<unit>()
        c.AddPaths(subject, PathType.Subject)
        let solution,_ = c.Execute(ClipType.Union, FillRule.NonZero)
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points after spike cleanup")
        Assert.IsTrue(abs (totalAbsArea subject - totalAbsArea solution) < 1e-9, "expected union area to match input area")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsLargeWithClosingVertexGivesEightPointPath () =
        // Identical geometry to the No-Closing-Vertex case, but each path repeats its
        // first vertex as an explicit closing vertex (as the Rhino/Euclid polylines do).
        // That redundant zero-length edge lets the sweep collapse the colinear spike, so
        // this resolves to 8 points. Same shape, different vertex count -> different result,
        // which is the engine fragility these two tests pin down.
        let subj = paths [ path [| -40000.0;            30000.000000000004
                                   -40000.0;            50000.0
                                   -62626.90989118975;  50000.00000000001
                                   -62626.90989118975;  30000.000000000004
                                   -40000.0;            30000.000000000004 |] ]
        let clip = paths [ path [| -50000.0;            50000.0
                                   -49999.99999999999;  70000.0
                                   -70000.0;            70000.0
                                   -70000.0;            40000.00000000001
                                   -62626.90989118976;  40000.00000000001
                                   -62626.90989118975;  50000.00000000001
                                   -50000.0;            50000.0 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsTinyGivesEightPointPath () =
        let subj = paths [ path [| -0.004;                0.0030000000000000005
                                   -0.004;                0.005
                                   -0.0062626909891189755; 0.005
                                   -0.0062626909891189755; 0.0030000000000000005 |] ]
        let clip = paths [ path [| -0.005;                0.005
                                   -0.005;                0.007
                                   -0.007;                0.007
                                   -0.007;                0.004
                                   -0.006262690989118976; 0.004
                                   -0.0062626909891189755; 0.005 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsTinySharedEdgeGivesEightPointPath () =
        // Two polygons that touch along a SHARED colinear edge (the x=-0.05 vertical
        // overlap plus the y=-0.0626 seam), all at sub-unit scale.
        let subj = paths [ path [| -0.030000000000000002; -0.039999999999999994
                                   -0.05000000000000001;   -0.039999999999999994
                                   -0.05000000000000001;   -0.06262690989118976
                                   -0.030000000000000006;  -0.06262690989118976 |] ]
        let clip = paths [ path [| -0.05000000000000001;   -0.049999999999999996
                                   -0.07;                  -0.049999999999999996
                                   -0.07000000000000002;   -0.06999999999999999
                                   -0.04000000000000001;   -0.07
                                   -0.04000000000000001;   -0.06262690989118977
                                   -0.05000000000000001;   -0.06262690989118976 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsTinySharedEdgeRotated180GivesEightPointPath () =
        let subj = paths [ path [| -0.030000000000000002; -0.039999999999999994
                                   -0.05000000000000001;   -0.039999999999999994
                                   -0.05000000000000001;   -0.06262690989118976
                                   -0.030000000000000006;  -0.06262690989118976 |] ]
        let clip = paths [ path [| -0.05000000000000001;   -0.049999999999999996
                                   -0.07;                  -0.049999999999999996
                                   -0.07000000000000002;   -0.06999999999999999
                                   -0.04000000000000001;   -0.07
                                   -0.04000000000000001;   -0.06262690989118977
                                   -0.05000000000000001;   -0.06262690989118976 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsTinySharedEdgeRotatedMinus90GivesEightPointPath () =
        let subj = paths [ path [| 0.353;             -0.030000000000000065
                                   0.353;             -0.050000000000000065
                                   0.3756269098911898; -0.05000000000000007
                                   0.3756269098911898; -0.03000000000000007 |] ]
        let clip = paths [ path [| 0.363;             -0.05000000000000007
                                   0.363;             -0.07000000000000008
                                   0.383;             -0.07000000000000008
                                   0.383;             -0.04000000000000007
                                   0.3756269098911898; -0.04000000000000007
                                   0.3756269098911898; -0.05000000000000007 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsSmallSharedEdgeRotated180GivesEightPointPath () =
        let subj = paths [ path [| -14.090000000000002; -0.3999999999999983
                                   -14.290000000000001; -0.39999999999999825
                                   -14.290000000000001; -0.6262690989118957
                                   -14.090000000000002; -0.6262690989118957 |] ]
        let clip = paths [ path [| -14.290000000000001; -0.4999999999999982
                                   -14.490000000000002; -0.4999999999999982
                                   -14.490000000000002; -0.6999999999999983
                                   -14.190000000000001; -0.6999999999999983
                                   -14.190000000000001; -0.6262690989118959
                                   -14.290000000000001; -0.6262690989118957 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsSharedEdgeRotatedMinus90GivesEightPointPath () =
        let subj = paths [ path [| 35.3;             -3.0000000000000067
                                   35.3;             -5.000000000000006
                                   37.56269098911898; -5.000000000000007
                                   37.56269098911898; -3.000000000000007 |] ]
        let clip = paths [ path [| 36.3;             -5.000000000000007
                                   36.3;             -7.000000000000007
                                   38.3;             -7.000000000000007
                                   38.3;             -4.000000000000007
                                   37.56269098911898; -4.000000000000007
                                   37.56269098911898; -5.000000000000007 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsRotatedTinyGivesEightPointPath () =
        // The shared-edge shapes rotated ~179.999 deg, at sub-unit scale: edges are now
        // very slightly off-axis, so no two edges are exactly colinear.
        let subj = paths [ path [| -0.00030000698127131514; -0.00039999476395132075
                                   -0.0005000069812408534;   -0.00039999127329281686
                                   -0.0005000109303816248;   -0.0006262603721702517
                                   -0.00030001093041208654;  -0.0006262638628287556 |] ]
        let clip = paths [ path [| -0.0005000087265701053;   -0.000499991273277586
                                   -0.0007000087265396436;   -0.0004999877826190821
                                   -0.0007000122171981475;   -0.0006999877825886203
                                   -0.0004000122172438401;   -0.0006999930185763761
                                   -0.00040001093039685566;  -0.0006262621174995036
                                   -0.0005000109303816248;   -0.0006262603721702517 |] ]
        // At ~5e-4 scale the rotation's off-axis vertex offsets are ~1e-9, so the default
        // CoordEqTolerance (1e-9) fuses genuinely-distinct vertices and the polygon collapses
        // to zero output. CoordEqTolerance must track coordinate magnitude (~maxCoord * 1e-6);
        // tightening it to 1e-10 keeps the vertices distinct so the union resolves correctly.
        // CoordEqTolerance is process-wide, so save and restore it for test isolation.
        let c = Clipper64<unit>()
        let prevCoord = c.CoordEqTolerance
        try
            c.CoordEqTolerance <- 1e-10
            c.AddSubject(subj)
            c.AddClip(clip)
            let solution,_ = c.Execute(ClipType.Union, FillRule.NonZero)
            Assert.AreEqual(1, solution.Count, "expected a single merged path")
            Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")
        finally
            c.CoordEqTolerance <- prevCoord

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsRotatedGivesEightPointPath () =
        // Same rotated shapes at ~0.5 scale.
        let subj = paths [ path [| -0.30000698127131514; -0.3999947639513207
                                   -0.5000069812408534;   -0.39999127329281686
                                   -0.5000109303816248;   -0.6262603721702515
                                   -0.3000109304120866;   -0.6262638628287555 |] ]
        let clip = paths [ path [| -0.5000087265701053;   -0.49999127327758597
                                   -0.7000087265396436;   -0.4999877826190821
                                   -0.7000122171981474;   -0.6999877825886204
                                   -0.40001221724384006;  -0.6999930185763762
                                   -0.4000109303968557;   -0.6262621174995037
                                   -0.5000109303816248;   -0.6262603721702515 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsRotated115Scale001KeepsEightPointPath () =
        let subj = paths [ path [| -0.6492368853792919; 1.273982914588338
                                   -0.6576892506141059; 1.292109070329071
                                   -0.6758154063548388; 1.283656705094257
                                   -0.6631368585026178; 1.2564674714831574
                                   -0.6564545695224803; 1.2595834740086005
                                   -0.6606807521398872; 1.268646551878967
                                   -0.6492368853792919; 1.273982914588338 |] ]
        let clip = paths [ path [| -0.6317214422741114; 1.260082941465012
                                   -0.6401738075089254; 1.278209097205745
                                   -0.6606807521398872; 1.268646551878967
                                   -0.6522283869050733; 1.250520396138234
                                   -0.6317214422741114; 1.260082941465012 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsRotated115Scale001TranslatedKeepsEightPointPath () =
        let subj = paths [ path [| -0.9329112227217633; 1.141703398663499
                                   -0.9413635879565773; 1.159829554404232
                                   -0.9594897436973103; 1.151377189169418
                                   -0.9468111958450893; 1.1241879555583185
                                   -0.9401289068649517; 1.1273039580837616
                                   -0.9443550894823587; 1.136367035954128
                                   -0.9329112227217633; 1.141703398663499 |] ]
        let clip = paths [ path [| -0.9153957796165828; 1.1278034255401732
                                   -0.9238481448513969; 1.145929581280906
                                   -0.9443550894823587; 1.136367035954128
                                   -0.9359027242475448; 1.1182408802133952
                                   -0.9153957796165828; 1.1278034255401732 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")

    [<TestMethod>]
    member _.UnionTwoTouchingPolygonsRotated001MovedScale1KeepsEightPointPath () =
        let subj = paths [ path [| 142.72459223457366; 1005.0249254478591
                                   144.72459220411193; 1005.0252745137077
                                   144.7242431382633;  1007.025274483246
                                   141.7242431839559;  1007.0247508844731
                                   141.7243718686537;  1006.2874418848219
                                   142.7243718534228;  1006.2876164177462
                                   142.72459223457366; 1005.0249254478591 |] ]
        let clip = paths [ path [| 140.7247667979597;  1004.0245763972414
                                   142.72476676749795; 1004.02492546309
                                   142.7243718534228;  1006.2876164177462
                                   140.72437188388454; 1006.2872673518976
                                   140.7247667979597;  1004.0245763972414 |] ]
        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected a single merged path")
        Assert.AreEqual(8, solution.[0].PointCount, "expected 8 points in the merged path")


    [<TestMethod>]
    member _.OpenSubjectClippedAgainstClosedClip () =
        // open horizontal line through middle of a square
        let openSubj = paths [ path [| -5.0; 5.0; 15.0; 5.0 |] ]
        let clip = paths [ square 0.0 0.0 10.0 ]
        let c = Clipper64<unit>()
        c.AddOpenSubject(openSubj)
        c.AddClip(clip)
        let solutionClosed, solutionOpen = c.Execute(ClipType.Intersection, FillRule.NonZero)
        Assert.AreEqual(0, solutionClosed.Count, "no closed output expected")
        Assert.AreEqual(1, solutionOpen.Count, "expected the line clipped to one segment")
        let p = (roundPaths solutionOpen).[0]
        Assert.AreEqual(2, p.PointCount, "expected two endpoints")
