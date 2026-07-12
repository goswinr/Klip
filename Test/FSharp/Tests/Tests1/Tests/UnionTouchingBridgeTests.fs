namespace Klip.Tests

// the individual tolerance properties are [<Obsolete>]-hidden but exercised here on purpose
#nowarn "44"

open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.Tests.Helpers

[<TestClass>]
type UnionTouchingBridgeTests () =

    let rotateDegrees (degrees: float) (x: float, y: float) =
        let radians = degrees * System.Math.PI / 180.0
        let c = cos radians
        let s = sin radians
        (x * c - y * s, x * s + y * c)

    let transform scale rotation points =
        points
        |> Array.collect (fun (x, y) ->
            let x, y = rotateDegrees rotation (x * scale, y * scale)
            [| x; y |])
        |> path

    let inputPaths shift rotation scale =
        paths [
            transform scale rotation [|
                9.0, 35.0
                9.0, 37.0
                7.0, 37.0 + shift
                7.0, 34.0
                14.0, 34.0
                14.0, 37.0 + shift
                12.0, 37.0
                12.0, 35.0
            |]
            transform scale rotation [|
                7.0, 37.0 + shift
                8.0, 37.0
                8.0, 38.0
                7.0, 38.0
            |]
        ]

    let unionWithTolerance tolerance (ps: Paths64<unit>) =
        let c = Clipper64<unit>()
        c.CoordEqTolerance <- tolerance
        c.MergeVertexTolerance <- tolerance
        c.AddPaths(ps, PathType.Subject)
        c.Execute(ClipType.Union, FillRule.NonZero) |> fst

    let areaOk (a: Paths64<unit>) (b: Paths64<unit>) =
        let aa = totalAbsArea a
        let bb = totalAbsArea b
        abs (aa - bb) < (max aa bb) * 0.01

    [<TestMethod>]
    member _.UnionTouchingBridgeMixedHorizontalityClassificationMerges () =
        // Regression for the CoordEqTolerance cap on tolerance-horizontality: the seam's two
        // sides here have slope ratios shift/2 (H-polygon top edge, run 2) and shift/1 (bridge
        // edge, run 1), so for shift in (HorizontalAngleTolerance, 2*HorizontalAngleTolerance]
        // the long side used to classify horizontal while the short side stayed sloped. With
        // the shifted vertex below the flat run (rotation 180), doHorizontal consumed the
        // horizontal-classified side a scanbeam before the shared far-end scanline, stranding
        // the bridge - 2 output paths instead of the merged one. The cap makes both sides sweep
        // as sloped (endpoint-Y difference above point coincidence), restoring the merge via
        // the normal crossing machinery. Default HorizontalAngleTolerance is 1e-5.
        for scale in [ 0.1; 1.0; 1000.0 ] do
            for shift in [ 1.05e-5; 1.5e-5; 1.99e-5 ] do
                for rotation in [ 0.0; 180.0 ] do
                    let input = inputPaths shift rotation scale
                    let result = unionWithTolerance (scale * 1e-5) input
                    Assert.IsTrue(
                        result.Count = 1 && result[0].PointCount = 11 && areaOk input result,
                        sprintf
                            "scale %g, shift %g, rotation %g: expected one 11-point polygon with preserved area, got %d path(s)%s"
                            scale shift rotation result.Count
                            (if result.Count = 0 then "" else sprintf ", first has %d point(s), area ok: %b" result[0].PointCount (areaOk input result)))

    [<TestMethod>]
    member _.UnionTouchingBridgeSweepProducesSingleElevenPointPolygon () =
        for scale in [ 0.1; 1.0; 100.0; 1000.0; 10000.0 ] do
            for shiftPower = -9 to -2 do
                for rotation in [ 0.0; 0.0001; 0.001; 0.01; 0.1; 45.0; 90.0; 180.0 ] do
                    let shift = 10.0 ** float shiftPower
                    let input = inputPaths shift rotation scale
                    let result = unionWithTolerance (scale * 1e-5) input
                    let message =
                        sprintf
                            "scale %g, shift 1e%d (%g), rotation %g: expected one 11-point polygon with preserved area, got %d path(s)%s"
                            scale
                            shiftPower
                            shift
                            rotation
                            result.Count
                            (if result.Count = 0 then "" else sprintf ", first has %d point(s), area ok: %b" result[0].PointCount (areaOk input result))

                    Assert.IsTrue(
                        result.Count = 1 && result[0].PointCount = 11 && areaOk input result,
                        message
                    )
