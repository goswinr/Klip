namespace Klip.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.Tests.Helpers

[<TestClass>]
type OffsetTests () =

    [<TestMethod>]
    member _.EmptyPathOffsetReturnsEmpty () =
        // Mirrors TS 'should handle empty path offset operation'.
        let solution = Paths64<unit>()
        let co = ClipperOffset<unit>()
        co.Execute(10.0, solution)
        Assert.AreEqual(0, solution.Count)

    [<TestMethod>]
    member _.InflateIdenticalPointPolygonReturnsEmpty () =
        // Mirrors TS 'inflatePathsD should not throw on a zero-area ring'.
        let subject =
            paths [
                path [| 496.0; 253.0
                        496.0; 253.0
                        496.0; 253.0
                        496.0; 253.0 |]
            ]

        let solution = Klipper.offsetPaths(subject, 2.0, JoinType.Round, 2.0, 0.0)

        Assert.AreEqual(0, solution.Count)

    [<TestMethod>]
    member _.NegativeOffsetEliminatesSmallPolygon () =
        // Mirrors first half of TS 'should handle negative offset operations correctly'.
        let subject = paths [ path [| 0.0; 0.0; 100.0; 0.0; 100.0; 100.0; 0.0; 100.0 |] ]
        let solution = Klipper.offsetPaths(subject, -50.0, JoinType.Miter, 2.0, 0.0)
        Assert.AreEqual(0, solution.Count, "a -50 offset should eliminate a 100x100 polygon")

    [<TestMethod>]
    member _.PositiveOffsetWithHolePreservesPolygon () =
        // Mirrors second half of TS 'should handle negative offset operations correctly'.
        let subjectWithHole =
            paths [
                path [|   0.0;   0.0; 100.0;   0.0; 100.0; 100.0;   0.0; 100.0 |] // outer
                path [|  40.0;  60.0;  60.0;  60.0;  60.0;  40.0;  40.0;  40.0 |] // hole
            ]
        let solution = Klipper.offsetPaths(subjectWithHole, 10.0, JoinType.Miter, 2.0, 0.0)
        Assert.AreEqual(1, solution.Count)

    [<TestMethod>]
    member _.PositiveOffsetWithReversedOrientationPreservesPolygon () =
        // Same shape as above, but with both contours reversed in winding direction.
        let reversedSubject =
            paths [
                path [|   0.0; 100.0; 100.0; 100.0; 100.0;   0.0;   0.0;   0.0 |] // reversed outer
                path [|  40.0;  40.0;  60.0;  40.0;  60.0;  60.0;  40.0;  60.0 |] // reversed hole
            ]
        let solution = Klipper.offsetPaths(reversedSubject, 10.0, JoinType.Miter, 2.0, 0.0)
        Assert.AreEqual(1, solution.Count)

    [<TestMethod>]
    member _.JoinTypesProduceConsistentSinglePolygon () =
        // Mirrors TS 'should produce correct results for different join types'.
        let subject = paths [ path [| 0.0; 0.0; 50.0; 0.0; 50.0; 50.0; 0.0; 50.0 |] ]
        let delta = 10.0

        let miter = Klipper.offsetPaths(subject, delta, JoinType.Miter, 2.0, 0.0)
        Assert.AreEqual(1, miter.Count)
        let miterCount = miter[0].PointCount

        let round = Klipper.offsetPaths(subject, delta, JoinType.Round, 2.0, 0.0)
        Assert.AreEqual(1, round.Count)
        Assert.IsTrue(
            round[0].PointCount >= miterCount,
            sprintf "round join (%d pts) should not have fewer vertices than miter join (%d pts)" round[0].PointCount miterCount)

        let bevel = Klipper.offsetPaths(subject, delta, JoinType.Bevel, 2.0, 0.0)
        Assert.AreEqual(1, bevel.Count)

        let square = Klipper.offsetPaths(subject, delta, JoinType.Square, 2.0, 0.0)
        Assert.AreEqual(1, square.Count)

    [<TestMethod>]
    member _.OpenPathOffsetWithDifferentEndTypes () =
        // Mirrors TS 'should handle open path offsetting correctly'.
        let openPath =
            paths [ path [| 0.0; 50.0; 20.0; 50.0; 40.0; 50.0; 60.0; 50.0; 80.0; 50.0; 100.0; 50.0 |] ]
        let delta = 10.0

        let butt = Klipper.offsetBothSides(openPath, delta, JoinType.Round, EndType.Butt, 2.0, 0.0)
        Assert.AreEqual(1, butt.Count)

        let round = Klipper.offsetBothSides(openPath, delta, JoinType.Round, EndType.Round, 2.0, 0.0)
        Assert.AreEqual(1, round.Count)

        let square = Klipper.offsetBothSides(openPath, delta, JoinType.Round, EndType.Square, 2.0, 0.0)
        Assert.AreEqual(1, square.Count)

        // Offsetting an open path produces a closed polygon enclosing the line.
        for p in square do
            Assert.IsTrue(p.PointCount >= 4, sprintf "expected at least 4 vertices, got %d" p.PointCount)

    [<TestMethod>]
    member _.RoundedOffsetRespectsArcTolerance () =
        // Mirrors TS 'should respect arc tolerance in rounded offsets'.
        let scale = 10.0
        let delta = 10.0 * scale
        let arcTolerance = 0.25 * scale

        let subject =
            paths [ path [| 50.0; 50.0; 100.0; 50.0; 100.0; 150.0; 50.0; 150.0; 0.0; 100.0 |] ]
        let scaledSubject = Paths64.scaleUp scale subject

        let co = ClipperOffset<unit>(2.0, arcTolerance)
        co.AddPaths(scaledSubject, JoinType.Round, EndType.Polygon)
        let solution = Paths64<unit>()
        co.Execute(delta, solution)

        Assert.AreEqual(1, solution.Count)
        // Round joins should not create excessive vertices.
        Assert.IsTrue(
            solution[0].PointCount <= 21,
            sprintf "expected <= 21 vertices, got %d" solution[0].PointCount)

        // The midpoint of every offset edge close to a subject vertex should sit
        // approximately delta away from that vertex (within arcTolerance).
        let originalPath = scaledSubject[0]
        let offsetPath = solution[0]
        let mutable minDistance = Double.MaxValue
        let oc = originalPath.PointCount
        let fc = offsetPath.PointCount
        for j = 0 to oc - 1 do
            let sx = originalPath.GetX(j)
            let sy = originalPath.GetY(j)
            for i = 0 to fc - 1 do
                let p1x = offsetPath.GetX(i)
                let p1y = offsetPath.GetY(i)
                let nextI = if i = fc - 1 then 0 else i + 1
                let p2x = offsetPath.GetX(nextI)
                let p2y = offsetPath.GetY(nextI)
                let mx = (p1x + p2x) / 2.0
                let my = (p1y + p2y) / 2.0
                let dx = mx - sx
                let dy = my - sy
                let d = sqrt (dx * dx + dy * dy)
                if d < delta * 2.0 && d < minDistance then
                    minDistance <- d

        Assert.IsTrue(
            minDistance + 1.0 >= delta - arcTolerance,
            sprintf "expected minDistance+1 >= %f, got %f" (delta - arcTolerance) (minDistance + 1.0))
