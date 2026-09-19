namespace Klip.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.Tests.Helpers

[<TestClass>]
type SnapToleranceTests () =
    [<TestMethod>]
    member _.SnapUsesAdjacentVerticesOnceRegardlessOfWindingOrStartVertex () =
        let vertices = [|0.,0.; 0.75,10.; 1.5,20.; 10.,30.|]
        for winding in [vertices; Array.rev vertices] do
            for start = 0 to 3 do
                let p = path [|for i = 0 to 3 do
                                  let x,y = winding[(start+i)%4]
                                  yield x; yield y|]
                Snap.xAndYSingle 1. (paths [p])
                for i = 0 to 3 do
                    let expected = if p.GetY i <= 10. then 0.375 elif p.GetY i = 20. then 1.5 else 10.
                    Assert.AreEqual(expected, p.GetX i, "sorted clusters are bounded by their minimum, not chained transitively")

    [<TestMethod>]
    member _.ZeroSnapAndIdenticalLargeCoordinatesStayBitExact () =
        for tolerance in [0.; 1e-5] do
            for x in [1e12 + 0.1; 1e308] do
                let input = paths [for _ = 1 to 5 do yield path [|x;0.; x;10.; x;20.|]]
                Snap.xAndYSingle tolerance input
                for p in input do
                    for i = 0 to p.PointCount-1 do Assert.AreEqual(x, p.GetX i)

    [<TestMethod>]
    member _.SnapRejectsInvalidToleranceAndCoordinatesBeforeMutatingAnyPath () =
        let first () = path [|0.;0.; 0.5;10.; 10.;20.|]
        for tolerance in [-1.; Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity] do
            let p = first ()
            let before = p.XYs.ToArray()
            Assert.ThrowsException<ArgumentException>(Action(fun () -> Snap.xAndYSingle tolerance (paths [p]))) |> ignore
            CollectionAssert.AreEqual(before, p.XYs.ToArray())
        for invalid in [Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity] do
            let p = first ()
            let before = p.XYs.ToArray()
            Assert.ThrowsException<ArgumentException>(Action(fun () -> Snap.xAndYSingle 1. (paths [p; path [|invalid;0.; 1.;1.|]]))) |> ignore
            CollectionAssert.AreEqual(before, p.XYs.ToArray())

    [<TestMethod>]
    member _.SnapPreservesMetadataAndUsesAnInclusiveClusterBoundary () =
        let p = Path64<string>(ResizeArray [0.;0.; 1.;10.; 10.;20.], Some (ResizeArray ["a";"b";"c"]))
        Snap.xAndYSingle 1. (ResizeArray [p])
        Assert.AreEqual(0.5, p.GetX 0)
        Assert.AreEqual(0.5, p.GetX 1)
        CollectionAssert.AreEqual([|"a";"b";"c"|], p.Zs.Value.ToArray())
