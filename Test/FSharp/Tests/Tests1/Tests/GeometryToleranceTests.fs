namespace Klip.Tests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.KlipInternalTypes
open Klip.Tests.Helpers

/// Topology must not inherit the angle used to simplify nearly straight runs.
/// Check both containment representations: input paths and output vertex rings.
[<TestClass>]
type GeometryToleranceTests () =
    let coordTol = 1e-5

    let asRing (polygon: Path64<unit>) =
        let points : OutPt<unit>[] = Array.init polygon.PointCount (fun i ->
            { x = polygon.GetX i; y = polygon.GetY i; z = ()
              next = Unchecked.defaultof<_>; prev = Unchecked.defaultof<_>
              outrec = Unchecked.defaultof<_>; horz = Unchecked.defaultof<_> })
        for i = 0 to points.Length - 1 do
            points[i].next <- points[(i + 1) % points.Length]
            points[i].prev <- points[(i + points.Length - 1) % points.Length]
        points[0]

    let assertContainmentWithin tolerance expected x y polygon =
        // Start-vertex rotation and winding must not change a containment answer.
        for winding in [polygon; Geo.reversePath polygon] do
            for start = 0 to winding.PointCount - 1 do
                let rotated = path [| for offset = 0 to winding.PointCount - 1 do
                                          let i = (start + offset) % winding.PointCount
                                          yield winding.GetX i
                                          yield winding.GetY i |]
                let context = sprintf "point (%g,%g), start %d, signed area %g" x y start winding.SignedArea
                Assert.AreEqual(expected, Geo.pointInPolygon(tolerance, x, y, rotated), "Path64: " + context)
                Assert.AreEqual(expected, Eng.pointInOpPolygon tolerance x y (asRing rotated), "OutPt: " + context)

    let assertContainment expected x y polygon =
        assertContainmentWithin coordTol expected x y polygon

    [<TestMethod>]
    member _.AngularCollinearityDoesNotTurnRightAnglesIntoStraightLinesAtExtremeScales () =
        for scale in [1e-200; 1e-90; 1.; 1e90; 1e200] do
            Assert.IsFalse(Geo.isColinear(1e-6, 0.,0., scale,0., scale,scale), sprintf "right angle at scale %g" scale)
            Assert.IsTrue(Geo.isColinear(1e-6, 0.,0., scale,scale, 0.,0.), "a true reversal remains collinear")
            Assert.IsTrue(Geo.isColinear(0., 0.,0., scale,0., 2.*scale,0.), "exact straight run")
            Assert.AreEqual(-1, Geo.dotProductSign(0.,0., scale,scale, 0.,0.), "a reversal retains its sign even when its raw dot product underflows")
            Assert.AreEqual(1, Geo.dotProductSign(0.,0., scale,scale, 2.*scale,2.*scale))

    [<TestMethod>]
    member _.ClosedRingValidationRejectsTooFewVerticesAndAppliesTheTriangleWindow () =
        let check tolerance coords expected =
            Assert.AreEqual(expected, Eng.isValidClosedPath(tolerance, asRing (path coords)))
        check 0. [|0.;0.|] false
        check 0. [|0.;0.; 10.;10.|] false
        check 1. [|0.;0.; 0.5;0.; 0.;10.|] false
        check 1. [|0.;0.; 1.;0.; 0.;10.|] true // strict threshold
        check 0. [|0.;0.; 0.5;0.; 0.;10.|] true
        check 1. [|0.;0.; 0.5;0.; 10.;10.; 0.;10.|] true // only triangles are culled

    [<TestMethod>]
    member _.OutputDeduplicationCannotReturnAClosedPathWithFewerThanThreeVertices () =
        let ring = asRing (path [|0.;0.; 0.5;0.; 0.;0.5|])
        let output = path [||]
        Assert.IsFalse(Eng.buildPath(ring, false, false, output, 1., 0.))
        Assert.IsFalse(Eng.buildPath(ring, false, true, output, 1., 0.), "open output still needs two distinct vertices")

    [<TestMethod>]
    member _.PointsSeventyUnitsFromLongDiagonalAreNotOnItsBoundary () =
        // Both points are ~70.7 units from the diagonal: far beyond 1e-5.
        // An angular zero test used to call both of them IsOn.
        let polygon = path [| 0.;0.; 1e6;1e6; 0.;1e6 |]
        assertContainment PointInPolygonResult.IsInside 100000. 100100. polygon
        assertContainment PointInPolygonResult.IsOutside 100100. 100000. polygon

    [<TestMethod>]
    member _.ContainmentNearDiagonalEndpointDoesNotDependOnWinding () =
        // The angular normalization used different distances to the endpoint on
        // reversal, changing IsOn to IsInside/IsOutside for these same points.
        let polygon = path [| 0.;0.; 1e6;1e6; 0.;1e6 |]
        assertContainment PointInPolygonResult.IsInside 1000. 1010. polygon
        assertContainment PointInPolygonResult.IsOutside 1000. 990. polygon

    [<TestMethod; Timeout(5000)>]
    member _.BoundaryDistanceToleranceAppliesToSlopedAndAxisAlignedEdges () =
        let square = path [| 0.;0.; 10.;0.; 10.;10.; 0.;10. |]
        for x,y in [5.,-0.5*coordTol; 10.+0.5*coordTol,5.; 5.,10.+0.5*coordTol; -0.5*coordTol,5.] do
            assertContainment PointInPolygonResult.IsOn x y square
        assertContainment PointInPolygonResult.IsOutside (10.+2.*coordTol) 5. square
        let triangle = path [| 0.;0.; 10.;10.; 0.;10. |]
        assertContainment PointInPolygonResult.IsOn 5. (5.+0.5*coordTol) triangle
        assertContainment PointInPolygonResult.IsInside 5. (5.+2.*coordTol) triangle
        assertContainment PointInPolygonResult.IsOutside 5. (5.-2.*coordTol) triangle

    [<TestMethod>]
    member _.ZeroToleranceRecognizesAnExactlyCollinearPointWithoutDirectionRounding () =
        // (1,49) is exactly one third of (3,147). Normalizing the edge by 147
        // introduces a rounding residual, so zero tolerance needs the raw determinant.
        let triangle = path [| 0.;0.; 3.;147.; 0.;147. |]
        assertContainmentWithin 0. PointInPolygonResult.IsOn 1. 49. triangle

    [<TestMethod>]
    member _.IncreasingBoundaryToleranceCannotDislodgeAnExactlyCollinearPoint () =
        let triangle = path [| 0.;0.; 3.;147.; 0.;147. |]
        for tolerance in [0.; 1e-20; 1e-16; 1e-12; 1e-5] do
            assertContainmentWithin tolerance PointInPolygonResult.IsOn 1. 49. triangle

    [<TestMethod>]
    member _.AngularCleanupPreservesThinTrianglesWhoseHeightExceedsDistanceTolerance () =
        for tolerance in [0.; 1e-5] do
            for preserve in [false; true] do
                for polygon in [path [|0.;0.; 1e6;1e6; 500000.;500001.|]
                                path [|500000.;500001.; 1e6;1e6; 0.;0.|]] do
                    let c = Clipper64<unit>()
                    c.Tolerance <- tolerance
                    c.PreserveColinear <- preserve
                    c.AddSubject(paths [polygon])
                    let result, _ = c.Execute(ClipType.Union, FillRule.NonZero)
                    Assert.AreEqual(1, result.Count, "a small turn angle alone must not erase a real triangle")
                    Assert.AreEqual(500000., totalAbsArea result, 1e-4)

    [<TestMethod>]
    member _.ShortSegmentCrossingLongDiagonalIsAProperIntersectionInEitherDirection () =
        let diagonal = [0.,0.,1e6,1e6; 1e6,1e6,0.,0.]
        let crossing = [5e5,5e5-1.,5e5,5e5+1.; 5e5,5e5+1.,5e5,5e5-1.]
        for ax,ay,bx,by in diagonal do
            for cx,cy,dx,dy in crossing do
                Assert.IsTrue(Geo.segsIntersectNotInclusive(ax,ay,bx,by,cx,cy,dx,dy), "crossing at (500000,500000)")
                Assert.IsTrue(Geo.segsIntersectNotInclusive(cx,cy,dx,dy,ax,ay,bx,by), "swapping segments preserves the crossing")
        Assert.IsFalse(Geo.segsIntersectNotInclusive(0.,0.,1e6,1e6,5e5,5e5,5e5,5e5+1.), "endpoint-only contact is not a proper crossing")
        Assert.IsFalse(Geo.segsIntersectNotInclusive(0.,0.,1e6,1e6,1.,1.,2.,2.), "collinear overlap is not a proper crossing")
