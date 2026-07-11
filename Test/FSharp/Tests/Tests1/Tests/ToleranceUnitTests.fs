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
        c.ColinearityTolerance <- 0.5
        c.HorizontalAngleTolerance <- 1e-4
        c.NearTopYToleranceFactor <- 0.5
        c.Tolerance <- 42.0
        Assert.AreEqual(0.5, c.ColinearityTolerance)
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

        // Sanity: with the *default* (unscaled) tolerances the same tiny input is mangled -
        // the whole fixture spans ~0.23 units, below the 2.0 sliver-cull window - so the
        // tolerance is doing real work above, not vacuously passing.
        let mangled = unionWithTolerance None (scalePaths s (subject ())) (scalePaths s (clip ()))
        let sameShape =
            mangled.Count = baseline.Count
            && abs (totalAbsArea mangled - totalAbsArea baseline * s * s) <= totalAbsArea baseline * s * s * 1e-9
        Assert.IsFalse(sameShape, "default tolerances at tiny scale should not reproduce the correctly-scaled result")

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
