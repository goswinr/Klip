namespace Klip.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.Tests.Helpers

/// Tests for the API contract fixes:
/// - empty input paths raise ArgumentException (instead of crashing or NaN-poisoning the sweep)
/// - ClipType.NoClip executes without raising
/// - the high-level wrappers do not mutate their input lists
[<TestClass>]
type ApiContractTests () =

    // counter-clockwise = positive orientation (Y up)
    let posSquare () = path [| 0.0;0.0; 10.0;0.0; 10.0;10.0; 0.0;10.0 |]
    // clockwise = negative orientation
    let negSquare () = path [| 0.0;0.0; 0.0;10.0; 10.0;10.0; 10.0;0.0 |]

    [<TestMethod>]
    member _.EmptyPathInAddPathsRaisesArgumentException () =
        let c = Clipper64<unit>()
        let withEmpty = paths [ posSquare(); Path64.createEmpty() ]
        Assert.ThrowsException<ArgumentException>(
            Action(fun () -> c.AddPaths(withEmpty, PathType.Subject))) |> ignore

    [<TestMethod>]
    member _.EmptyPathInKlipperWrapperRaisesArgumentException () =
        let withEmpty = paths [ Path64.createEmpty() ]
        Assert.ThrowsException<ArgumentException>(
            Action(fun () -> Klipper.unionSelf withEmpty |> ignore)) |> ignore

    [<TestMethod>]
    member _.NoClipExecutesWithoutRaising () =
        let c = Clipper64<unit>()
        c.AddPaths(paths [ posSquare() ], PathType.Subject)
        let closed, _ = c.Execute(ClipType.NoClip, FillRule.NonZero)
        Assert.AreEqual(0, closed.Count)

    [<TestMethod>]
    member _.UnionSelfCheckedDoesNotMutateInput () =
        let neg = negSquare()
        let pos = posSquare()
        let input = paths [ neg; pos ]
        let areasBefore = [| input[0].SignedArea; input[1].SignedArea |]
        let result = Klipper.unionSelfChecked input
        // same instances, same order, same orientation as before the call
        Assert.IsTrue(Object.ReferenceEquals(input[0], neg), "input[0] must be the original instance")
        Assert.IsTrue(Object.ReferenceEquals(input[1], pos), "input[1] must be the original instance")
        Assert.AreEqual(areasBefore[0], input[0].SignedArea)
        Assert.AreEqual(areasBefore[1], input[1].SignedArea)
        Assert.IsTrue(result.Count > 0, "union should produce output")

    [<TestMethod>]
    member _.EnsurePositiveOrientationsDoesNotMutateInput () =
        let neg = negSquare()
        let pos = posSquare()
        let input = paths [ neg; pos ]
        let result = Paths64.ensurePositiveOrientations input
        Assert.IsFalse(Object.ReferenceEquals(input, result), "must return a new list")
        Assert.IsTrue(input[0].SignedArea < 0.0, "input path must keep its orientation")
        Assert.IsTrue(result[0].SignedArea > 0.0, "result path must be reversed")
        Assert.IsTrue(Object.ReferenceEquals(result[1], pos), "already-positive paths are reused")

    [<TestMethod>]
    member _.EnsureNegativeOrientationsDoesNotMutateInput () =
        let neg = negSquare()
        let pos = posSquare()
        let input = paths [ neg; pos ]
        let result = Paths64.ensureNegativeOrientations input
        Assert.IsFalse(Object.ReferenceEquals(input, result), "must return a new list")
        Assert.IsTrue(input[1].SignedArea > 0.0, "input path must keep its orientation")
        Assert.IsTrue(result[1].SignedArea < 0.0, "result path must be reversed")
        Assert.IsTrue(Object.ReferenceEquals(result[0], neg), "already-negative paths are reused")
