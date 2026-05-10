namespace Klip.Tests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.KlipInternal
open Klip.Tests.Helpers

[<TestClass>]
type PolyTreeTests () =

    [<TestMethod>]
    member _.UnionToPolyTreeNests () =
        // Outer 20x20 square containing a 10x10 square hole pattern via a CCW + CW path
        // is awkward to express via union, so we instead use a complex multi-path union
        // and verify the tree has at least one outer contour with at least one hole.
        let outer = path [| 0.0;0.0; 100.0;0.0; 100.0;100.0; 0.0;100.0 |]
        // a smaller CCW square wholly inside outer (with NonZero fill, an inner CCW
        // contour subtracts when wound oppositely). To get a hole reliably we use
        // EvenOdd: two nested rings → outer ring + inner hole.
        let inner = path [| 25.0;25.0; 75.0;25.0; 75.0;75.0; 25.0;75.0 |]
        let subj = paths [ outer; inner ]

        let tree = PolyTree64<unit>()
        Clipper.booleanOpWithPolyTree(
            ClipType.Union, subj, null, tree, FillRule.EvenOdd, None)

        Assert.AreEqual(1, tree.Count, "expected one outer contour at top level")
        let outerNode = tree.Child(0)
        Assert.AreEqual(1, outerNode.Count, "expected one hole inside the outer contour")
        let holeNode = outerNode.Child(0)
        Assert.IsTrue(holeNode.IsHole, "child should report as a hole")

    [<TestMethod>]
    member _.PolyTreeToPathsFlattensTree () =
        let outer = path [| 0.0;0.0; 100.0;0.0; 100.0;100.0; 0.0;100.0 |]
        let inner = path [| 25.0;25.0; 75.0;25.0; 75.0;75.0; 25.0;75.0 |]
        let subj = paths [ outer; inner ]

        let tree = PolyTree64<unit>()
        Clipper.booleanOpWithPolyTree(
            ClipType.Union, subj, null, tree, FillRule.EvenOdd, None)

        let flat = Clipper.polyTreeToPaths64 tree
        Assert.AreEqual(2, flat.Count, "expected outer + hole as two flat paths")

        // outer area 100*100=10000, hole area 50*50=2500, tree area = 7500
        let treeArea = abs (tree.Area())
        Assert.IsTrue(abs (treeArea - 7500.0) < 1.0,
            sprintf "expected tree area ~7500, got %f" treeArea)

    [<TestMethod>]
    member _.PolyTreeFromUnionOfDisjointPolygonsHasTwoTopLevelChildren () =
        let a = path [| 0.0;0.0; 10.0;0.0; 10.0;10.0; 0.0;10.0 |]
        let b = path [| 100.0;100.0; 110.0;100.0; 110.0;110.0; 100.0;110.0 |]
        let subj = paths [ a; b ]

        let tree = PolyTree64<unit>()
        Clipper.booleanOpWithPolyTree(
            ClipType.Union, subj, null, tree, FillRule.NonZero, None)

        Assert.AreEqual(2, tree.Count)
        Assert.AreEqual(0, tree.Child(0).Count)
        Assert.AreEqual(0, tree.Child(1).Count)
