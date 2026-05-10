namespace Klip.Tests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.Tests.Helpers

[<TestClass>]
type BooleanTests () =

    let square (x: float) (y: float) (size: float) =
        path [| x;       y
                x+size;  y
                x+size;  y+size
                x;       y+size |]

    [<TestMethod>]
    member _.UnionTwoOverlappingSquaresGivesOnePolygon () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let solution = Clipper.union clip subj
        Assert.AreEqual(1, solution.Count, "expected one merged polygon")
        // two 10x10 squares overlapping by 5x5 -> 100 + 100 - 25 = 175
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 175.0) < 0.5,
            sprintf "expected union area ~175, got %f" area)

    [<TestMethod>]
    member _.IntersectTwoOverlappingSquaresGives5x5Square () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let solution = Clipper.intersect clip subj
        Assert.AreEqual(1, solution.Count)
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 25.0) < 0.5,
            sprintf "expected intersection area 25, got %f" area)

    [<TestMethod>]
    member _.DifferenceTwoOverlappingSquaresArea () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let solution = Clipper.difference clip subj
        Assert.AreEqual(1, solution.Count)
        // 100 - 25 = 75
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 75.0) < 0.5,
            sprintf "expected difference area 75, got %f" area)

    [<TestMethod>]
    member _.XorTwoOverlappingSquaresArea () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let solution = Clipper.xor clip subj
        // expected: two L-shapes (or one polygon with hole) — total area = 75 + 75 = 150
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 150.0) < 0.5,
            sprintf "expected xor area 150, got %f" area)

    [<TestMethod>]
    member _.UnionDisjointSquaresStaysSeparate () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 100.0 100.0 10.0 ]
        let solution = Clipper.union clip subj
        Assert.AreEqual(2, solution.Count, "disjoint squares should remain separate")
        let area = totalAbsArea solution
        Assert.IsTrue(abs (area - 200.0) < 0.5)

    [<TestMethod>]
    member _.UnionSelfResolvesSelfIntersectingPath () =
        // bow-tie: two triangles sharing a self-intersection point in the middle
        let subj = paths [ path [| 0.0;0.0; 10.0;10.0; 10.0;0.0; 0.0;10.0 |] ]
        let solution = Clipper.unionSelf subj
        Assert.IsTrue(solution.Count >= 1, "expected at least one polygon")
        let area = totalAbsArea solution
        // each triangle is 25, total 50
        Assert.IsTrue(abs (area - 50.0) < 0.5,
            sprintf "expected resolved bow-tie area 50, got %f" area)

    [<TestMethod>]
    member _.EmptySubjectUnionReturnsClip () =
        let subj = Paths64<unit>()
        let clip = paths [ square 0.0 0.0 10.0 ]
        let solution = Clipper.union clip subj
        // empty subject + non-empty clip union = clip
        Assert.AreEqual(1, solution.Count)
        Assert.IsTrue(abs (totalAbsArea solution - 100.0) < 0.5)

    [<TestMethod>]
    member _.EmptySubjectIntersectIsEmpty () =
        let subj = Paths64<unit>()
        let clip = paths [ square 0.0 0.0 10.0 ]
        let solution = Clipper.intersect clip subj
        Assert.AreEqual(0, solution.Count)

    [<TestMethod>]
    member _.NullSubjectGivesEmptySolution () =
        let clip = paths [ square 0.0 0.0 10.0 ]
        let solution = Clipper.union clip null
        Assert.AreEqual(0, solution.Count)

    [<TestMethod>]
    member _.Clipper64ExecuteSucceedsAndProducesUnion () =
        let subj = paths [ square 0.0 0.0 10.0 ]
        let clip = paths [ square 5.0 5.0 10.0 ]
        let c = Clipper64<unit>()
        c.AddSubject(subj)
        c.AddClip(clip)
        let solution = Paths64<unit>()
        let ok = c.Execute(ClipType.Union, FillRule.NonZero, solution)
        Assert.IsTrue(ok, "Clipper64.Execute should succeed")
        Assert.AreEqual(1, solution.Count)

    [<TestMethod>]
    member _.OpenSubjectClippedAgainstClosedClip () =
        // open horizontal line through middle of a square
        let openSubj = paths [ path [| -5.0; 5.0; 15.0; 5.0 |] ]
        let clip = paths [ square 0.0 0.0 10.0 ]
        let c = Clipper64<unit>()
        c.AddOpenSubject(openSubj)
        c.AddClip(clip)
        let solutionClosed = Paths64<unit>()
        let solutionOpen = Paths64<unit>()
        let ok = c.Execute(ClipType.Intersection, FillRule.NonZero, solutionClosed, solutionOpen)
        Assert.IsTrue(ok)
        Assert.AreEqual(0, solutionClosed.Count, "no closed output expected")
        Assert.AreEqual(1, solutionOpen.Count, "expected the line clipped to one segment")
        let p = solutionOpen.[0]
        Assert.AreEqual(2, p.PointCount, "expected two endpoints")
