namespace Klip.Tests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.Tests.Helpers

/// Tests for `Clipper64.SetToleranceUnit`: one length drives the five scale-dependent
/// tolerances, and clipping is scale-equivariant — scaling all input coordinates by `s`
/// together with `SetToleranceUnit s` yields the identically scaled solution
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

    /// NonZero union via Clipper64, with an optional tolerance unit set before adding paths.
    let unionWithUnit (u: float option) (subj: Paths64<unit>) (clp: Paths64<unit>) : Paths64<unit> =
        let c = Clipper64<unit>()
        match u with
        | Some u -> c.SetToleranceUnit u
        | None -> ()
        c.AddSubject subj
        c.AddClip clp
        let closed, _ = c.Execute(ClipType.Union, FillRule.NonZero)
        closed

    [<TestMethod>]
    member _.UnitOneReproducesTheDefaults () =
        let c = Clipper64<unit>()
        // poke every scale-dependent tolerance away from its default first
        c.CoordEqTolerance <- 123.0
        c.MergeVertexTolerance <- 123.0
        c.NearTopYToleranceCap <- 123.0
        c.SmallTriangleTolerance <- 123.0
        c.SplitAreaTolerance <- 123.0
        c.SetToleranceUnit 1.0
        let d = Clipper64<unit>()
        Assert.AreEqual(d.CoordEqTolerance, c.CoordEqTolerance)
        Assert.AreEqual(d.MergeVertexTolerance, c.MergeVertexTolerance)
        Assert.AreEqual(d.NearTopYToleranceCap, c.NearTopYToleranceCap)
        Assert.AreEqual(d.SmallTriangleTolerance, c.SmallTriangleTolerance)
        Assert.AreEqual(d.SplitAreaTolerance, c.SplitAreaTolerance)

    [<TestMethod>]
    member _.DimensionlessTolerancesAreUntouched () =
        let c = Clipper64<unit>()
        c.ColinearityTolerance <- 0.5
        c.HorizontalAngleTolerance <- 1e-4
        c.NearTopYToleranceFactor <- 0.5
        c.SetToleranceUnit 42.0
        Assert.AreEqual(0.5, c.ColinearityTolerance)
        Assert.AreEqual(1e-4, c.HorizontalAngleTolerance)
        Assert.AreEqual(0.5, c.NearTopYToleranceFactor)

    [<TestMethod>]
    member _.UnitOutsideValidRangeRaisesArgumentException () =
        let c = Clipper64<unit>()
        Assert.ThrowsException<ArgumentException>(
            Action(fun () -> c.SetToleranceUnit(-1.0))) |> ignore
        Assert.ThrowsException<ArgumentException>(
            Action(fun () -> c.SetToleranceUnit(1e9))) |> ignore

    [<TestMethod>]
    member _.ScaledInputWithScaledUnitGivesBitExactScaledOutput () =
        // Power-of-two scale: multiplying floats by s only shifts exponents, so both the
        // scaled inputs and the scaled tolerances are exact and every branch in the engine
        // decides identically — the solution must match coordinate-for-coordinate, bitwise.
        let s = 1.0 / 16777216.0 // 2^-24, brings the fixture to sub-unit magnitude ~3
        let baseline = unionWithUnit None (subject ()) (clip ())
        let scaled = unionWithUnit (Some s) (scalePaths s (subject ())) (scalePaths s (clip ()))
        Assert.IsTrue(baseline.Count > 0, "baseline union should produce output")
        Assert.AreEqual(baseline.Count, scaled.Count, "path count")
        for i in 0 .. baseline.Count - 1 do
            Assert.AreEqual(baseline[i].PointCount, scaled[i].PointCount, sprintf "point count of path %d" i)
            for j in 0 .. baseline[i].PointCount - 1 do
                Assert.AreEqual(baseline[i].GetX j * s, scaled[i].GetX j, sprintf "x of point %d in path %d" j i)
                Assert.AreEqual(baseline[i].GetY j * s, scaled[i].GetY j, sprintf "y of point %d in path %d" j i)

        // Sanity: with the *default* (unscaled) tolerances the same tiny input is mangled —
        // the whole fixture spans ~0.23 units, below the 2.0 sliver-cull window — so the
        // unit is doing real work above, not vacuously passing.
        let mangled = unionWithUnit None (scalePaths s (subject ())) (scalePaths s (clip ()))
        let sameShape =
            mangled.Count = baseline.Count
            && abs (totalAbsArea mangled - totalAbsArea baseline * s * s) <= totalAbsArea baseline * s * s * 1e-9
        Assert.IsFalse(sameShape, "default tolerances at tiny scale should not reproduce the correctly-scaled result")

    [<TestMethod>]
    member _.ScaledInputWithScaledUnitGivesScaledOutput_DecimalScale () =
        // Decimal scale: scaling the inputs itself rounds (half an ulp per coordinate), so
        // equivariance holds to float noise rather than bit-exactly. Benign fixture with no
        // knife-edge coincidences; compare within a tolerance far below any real divergence.
        let subj = paths [ path [| 0.0;0.0; 100.0;0.0; 100.0;100.0; 0.0;100.0 |] ]
        let clp = paths [ path [| 50.0;-10.0; 160.0;40.0; 70.0;120.0 |] ]
        let s = 1e-7
        let baseline = unionWithUnit None subj clp
        let scaled = unionWithUnit (Some s) (scalePaths s subj) (scalePaths s clp)
        Assert.IsTrue(baseline.Count > 0, "baseline union should produce output")
        Assert.AreEqual(baseline.Count, scaled.Count, "path count")
        for i in 0 .. baseline.Count - 1 do
            Assert.AreEqual(baseline[i].PointCount, scaled[i].PointCount, sprintf "point count of path %d" i)
            for j in 0 .. baseline[i].PointCount - 1 do
                Assert.AreEqual(baseline[i].GetX j * s, scaled[i].GetX j, 1e-6 * s, sprintf "x of point %d in path %d" j i)
                Assert.AreEqual(baseline[i].GetY j * s, scaled[i].GetY j, 1e-6 * s, sprintf "y of point %d in path %d" j i)
