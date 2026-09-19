namespace Klip.Tests

// the individual tolerance properties are [<Obsolete>]-hidden but exercised here on purpose
#nowarn "44"

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.Tests.Helpers

/// Tests for the `Clipper64.Tolerance` property: one absolute tolerance drives the five
/// scale-dependent tolerances, and clipping is scale-equivariant - scaling all input
/// coordinates by `s` together with the tolerance yields the identically scaled solution
/// (bit-exact when `s` is a power of two).
[<TestClass>]
type ToleranceUnitTests () =

    // The sliver-triangle fixture (Clipper2 issue 1067): coordinate magnitude ~5e7 with
    // near-zero-area slivers, so the sliver culls and join guards all participate.
    let subject () =
        paths [
            path [| -45077288.0; -27835646.0
                    -45216220.0; -27853069.0
                    -44996290.0; -28378125.0 |]
        ]

    let clip () =
        paths [
            path [| -45943111.0; -27944226.0
                    -45990276.0; -27890686.0
                    -46034753.0; -27840198.0 |]
            path [| -44185329.0; -29939581.0
                    -45679436.0; -28243538.0
                    -47826654.0; -25806113.0 |]
            path [| -48000000.0; -29000000.0
                    -44185329.0; -29939581.0
                    -47826654.0; -25806113.0 |]
            path [| -45679436.0; -28243538.0
                    -45514581.0; -27890485.0
                    -45943111.0; -27944226.0 |]
        ]

    let scalePaths (s: float) (ps: Paths64<unit>) : Paths64<unit> =
        Paths64.mapXY (fun v -> v * s) ps

    /// NonZero union via Clipper64, with an optional absolute tolerance set before adding paths.
    let unionWithTolerance (t: float option) (subj: Paths64<unit>) (clp: Paths64<unit>) : Paths64<unit> =
        let c = Clipper64<unit>()
        match t with
        | Some t -> c.Tolerance <- t
        | None -> ()
        c.AddSubject subj
        c.AddClip clp
        let closed, _ = c.Execute(ClipType.Union, FillRule.NonZero)
        closed

    [<TestMethod>]
    member _.DefaultsMatchExplicitlySettingTheReportedTolerance () =
        let c = Clipper64<unit>()
        let before = c.CoordEqTolerance, c.MergeVertexTolerance, c.NearTopYToleranceCap, c.SmallTriangleTolerance, c.SplitAreaTolerance
        c.Tolerance <- c.Tolerance
        let after = c.CoordEqTolerance, c.MergeVertexTolerance, c.NearTopYToleranceCap, c.SmallTriangleTolerance, c.SplitAreaTolerance
        Assert.AreEqual(before, after, "assigning the reported default must not change hidden culling or join thresholds")

    [<TestMethod>]
    member _.ReassigningReportedTolerancesPreservesExpertOverrides () =
        let c = Clipper64<unit>()
        c.MergeVertexTolerance <- 0.25
        c.HorizontalAngleTolerance <- 1e-7
        c.Tolerance <- c.Tolerance
        c.AngleTolerance <- c.AngleTolerance
        Assert.AreEqual(0.25, c.MergeVertexTolerance)
        Assert.AreEqual(1e-7, c.HorizontalAngleTolerance)

    [<TestMethod>]
    member _.MergeDistanceRetainsSubnormalAndTinyPositiveToleranceValues () =
        let c = Clipper64<unit>()
        for tolerance in [Double.Epsilon; 1e-200; 1e-100; 1e-5] do
            c.MergeVertexTolerance <- tolerance
            Assert.AreEqual(tolerance, c.MergeVertexTolerance)

    [<TestMethod>]
    member _.TinyPositiveAngularOverridesAreNotRoundedToExactModeBySquaring () =
        let c = Clipper64<unit>()
        for tolerance in [Double.Epsilon; 1e-200; 1e-100; 1e-3] do
            c.ColinearityTolerance <- tolerance
            Assert.AreEqual(tolerance, c.ColinearityTolerance)
        c.ColinearityTolerance <- 1e-200
        Assert.IsTrue(c.AngleTolerance > 0.)

    [<TestMethod>]
    member _.AngleAndSineToleranceRangesRoundTripIncludingExactMode () =
        let c = Clipper64<unit>()
        c.AngleTolerance <- 0.
        c.ColinearityTolerance <- c.ColinearityTolerance
        Assert.AreEqual(0., c.ColinearityTolerance)
        for sine in [0.; 1e-3; 0.05; 0.1] do
            c.ColinearityTolerance <- sine
            let angle = c.AngleTolerance
            c.AngleTolerance <- angle
            Assert.AreEqual(sine, c.ColinearityTolerance, 1e-16)
        for invalid in [-1.; 0.10001; 1.; Double.NaN; Double.PositiveInfinity] do
            Assert.ThrowsException<ArgumentException>(Action(fun () -> c.ColinearityTolerance <- invalid)) |> ignore
        for invalid in [-1.; 5.74; Double.NaN; Double.PositiveInfinity] do
            Assert.ThrowsException<ArgumentException>(Action(fun () -> c.AngleTolerance <- invalid)) |> ignore

    [<TestMethod>]
    member _.CoordinateToleranceCannotChangeAfterInputDeduplicationUntilClearAll () =
        for add in [ (fun (c: Clipper64<unit>) p -> c.AddSubject p)
                     (fun c p -> c.AddClip p)
                     (fun c p -> c.AddOpenSubject p) ] do
            let c = Clipper64<unit>()
            add c (paths [path [|0.;0.; 1e-6;0.; 0.;1e-6|]])
            let before = c.Tolerance, c.MergeVertexTolerance, c.SplitAreaTolerance
            Assert.ThrowsException<InvalidOperationException>(Action(fun () -> c.Tolerance <- 1e-9)) |> ignore
            Assert.ThrowsException<InvalidOperationException>(Action(fun () -> c.CoordEqTolerance <- 1e-9)) |> ignore
            Assert.AreEqual(before, (c.Tolerance, c.MergeVertexTolerance, c.SplitAreaTolerance), "failed changes must be atomic")
            c.Tolerance <- c.Tolerance // harmless reassignment is allowed
            c.ClearAll()
            c.Tolerance <- 1e-9
            c.AddSubject(paths [path [|0.;0.; 1e-6;0.; 0.;1e-6|]])
            let result, _ = c.Execute(ClipType.Union, FillRule.NonZero)
            Assert.AreEqual(1, result.Count, "clearing and re-adding uses the new tolerance")

    [<TestMethod>]
    member _.DefaultUnionPreservesUnitAndSubunitTriangles () =
        // Integer-grid culls used to discard these ordinary float polygons.
        for side in [1.0; 0.01] do
            let triangle = paths [path [| 0.;0.; side;0.; 0.;side |]]
            let result = Klipper.unionSelf triangle
            Assert.AreEqual(1, result.Count, sprintf "triangle with side %g must survive default clipping" side)
            Assert.AreEqual(3, result[0].PointCount)
            Assert.AreEqual(side * side * 0.5, totalAbsArea result, side * side * 1e-12)

    [<TestMethod>]
    member _.AssigningReportedDefaultDoesNotChangeTriangleUnion () =
        let triangle = paths [path [| 0.;0.; 1.;0.; 0.;1. |]]
        let defaultResult = unionWithTolerance None triangle (paths [])
        let explicitResult = unionWithTolerance (Some (Clipper64<unit>().Tolerance)) triangle (paths [])
        Assert.AreEqual(1, defaultResult.Count, "the default must preserve a unit triangle")
        Assert.AreEqual(defaultResult.Count, explicitResult.Count)
        Assert.AreEqual(totalAbsArea defaultResult, totalAbsArea explicitResult)

    [<TestMethod>]
    member _.ToleranceSetsAllFiveScaleDependentTolerances () =
        let c = Clipper64<unit>()
        // poke every scale-dependent tolerance away first
        c.CoordEqTolerance <- 123.0
        c.MergeVertexTolerance <- 123.0
        c.NearTopYToleranceCap <- 123.0
        c.SmallTriangleTolerance <- 123.0
        c.SplitAreaTolerance <- 123.0
        c.Tolerance <- 0.25
        Assert.AreEqual(0.25, c.Tolerance)
        Assert.AreEqual(0.25, c.CoordEqTolerance)
        Assert.AreEqual(0.25, c.MergeVertexTolerance)
        Assert.AreEqual(0.25, c.NearTopYToleranceCap)
        Assert.AreEqual(0.25, c.SmallTriangleTolerance)
        Assert.AreEqual(0.0625, c.SplitAreaTolerance) // the tolerance squared

    [<TestMethod>]
    member _.DimensionlessTolerancesAreUntouched () =
        let c = Clipper64<unit>()
        c.ColinearityTolerance <- 0.05
        c.HorizontalAngleTolerance <- 1e-4
        c.NearTopYToleranceFactor <- 0.5
        c.Tolerance <- 42.0
        Assert.AreEqual(0.05, c.ColinearityTolerance)
        Assert.AreEqual(1e-4, c.HorizontalAngleTolerance)
        Assert.AreEqual(0.5, c.NearTopYToleranceFactor)

    [<TestMethod>]
    member _.ToleranceOutsideValidRangeRaisesArgumentException () =
        let c = Clipper64<unit>()
        Assert.ThrowsException<ArgumentException>(
            Action(fun () -> c.Tolerance <- -1.0)) |> ignore
        Assert.ThrowsException<ArgumentException>(
            Action(fun () -> c.Tolerance <- 2e12)) |> ignore
        // both ends of the valid range are accepted
        c.Tolerance <- 0.0
        c.Tolerance <- 1e12

    [<TestMethod>]
    member _.ScaledInputWithScaledToleranceGivesBitExactScaledOutput () =
        // Power-of-two scale: multiplying floats by s only shifts exponents, so both the
        // scaled inputs and the scaled tolerances are exact and every branch in the engine
        // decides identically - the solution must match coordinate-for-coordinate, bitwise.
        let s = 1.0 / 16777216.0 // 2^-24, brings the fixture to sub-unit magnitude ~3
        let t0 = 1.0 // geometry below one unit is noise at the fixture's ~5e7 magnitude
        let baseline = unionWithTolerance (Some t0) (subject ()) (clip ())
        let scaled = unionWithTolerance (Some (t0 * s)) (scalePaths s (subject ())) (scalePaths s (clip ()))
        Assert.IsTrue(baseline.Count > 0, "baseline union should produce output")
        Assert.AreEqual(baseline.Count, scaled.Count, "path count")
        for i in 0 .. baseline.Count - 1 do
            Assert.AreEqual(baseline[i].PointCount, scaled[i].PointCount, sprintf "point count of path %d" i)
            for j in 0 .. baseline[i].PointCount - 1 do
                Assert.AreEqual(baseline[i].GetX j * s, scaled[i].GetX j, sprintf "x of point %d in path %d" j i)
                Assert.AreEqual(baseline[i].GetY j * s, scaled[i].GetY j, sprintf "y of point %d in path %d" j i)

        // Sanity: keeping t0 unscaled collapses this ~0.23-unit fixture. This uses an
        // explicitly oversized tolerance, not a dependency on legacy integer-grid defaults.
        let mangled = unionWithTolerance (Some t0) (scalePaths s (subject ())) (scalePaths s (clip ()))
        let sameShape =
            mangled.Count = baseline.Count
            && abs (totalAbsArea mangled - totalAbsArea baseline * s * s) <= totalAbsArea baseline * s * s * 1e-9
        Assert.IsFalse(sameShape, "an unscaled tolerance at tiny scale should not reproduce the correctly-scaled result")

    [<TestMethod>]
    member _.ScaledInputWithScaledToleranceGivesScaledOutput_DecimalScale () =
        // Decimal scale: scaling the inputs itself rounds (half an ulp per coordinate), so
        // equivariance holds to float noise rather than bit-exactly. Benign fixture with no
        // knife-edge coincidences; compare within a tolerance far below any real divergence.
        let subj = paths [ path [| 0.0;0.0; 100.0;0.0; 100.0;100.0; 0.0;100.0 |] ]
        let clp = paths [ path [| 50.0;-10.0; 160.0;40.0; 70.0;120.0 |] ]
        let s = 1e-7
        let t0 = 1e-5
        let baseline = unionWithTolerance (Some t0) subj clp
        let scaled = unionWithTolerance (Some (t0 * s)) (scalePaths s subj) (scalePaths s clp)
        Assert.IsTrue(baseline.Count > 0, "baseline union should produce output")
        Assert.AreEqual(baseline.Count, scaled.Count, "path count")
        for i in 0 .. baseline.Count - 1 do
            Assert.AreEqual(baseline[i].PointCount, scaled[i].PointCount, sprintf "point count of path %d" i)
            for j in 0 .. baseline[i].PointCount - 1 do
                Assert.AreEqual(baseline[i].GetX j * s, scaled[i].GetX j, 1e-6 * s, sprintf "x of point %d in path %d" j i)
                Assert.AreEqual(baseline[i].GetY j * s, scaled[i].GetY j, 1e-6 * s, sprintf "y of point %d in path %d" j i)
