namespace Klip.Tests

open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.Tests.Helpers

/// F# port of `clipper2-ts/tests/sliver-triangle.test.ts`
/// (Clipper2 issue https://github.com/AngusJohnson/Clipper2/issues/1067).
///
/// Regression: a NonZero union of a subject triangle with several clip triangles
/// — two of them slivers with near-zero area — used to produce an output
/// polygon with total area far exceeding the sum of input areas, i.e. it
/// invented region outside any input shape.
[<TestClass>]
type SliverTriangleTests () =

    [<TestMethod>]
    member _.UnionWithSliverTrianglesDoesNotProduceAreaOutsideInputs () =
        let poly1 =
            paths [
                path [| -45077288.0; -27835646.0
                        -45216220.0; -27853069.0
                        -44996290.0; -28378125.0 |]
            ]

        let poly2 =
            paths [
                // Sliver
                path [| -45943111.0; -27944226.0
                        -45990276.0; -27890686.0
                        -46034753.0; -27840198.0 |]
                // Sliver
                path [| -44185329.0; -29939581.0
                        -45679436.0; -28243538.0
                        -47826654.0; -25806113.0 |]
                // Big triangle
                path [| -48000000.0; -29000000.0
                        -44185329.0; -29939581.0
                        -47826654.0; -25806113.0 |]
                // Small triangle
                path [| -45679436.0; -28243538.0
                        -45514581.0; -27890485.0
                        -45943111.0; -27944226.0 |]
            ]

        let inputArea = totalAbsArea poly1 + totalAbsArea poly2

        let result =
            Klipper.booleanOp(ClipType.Union, poly1, poly2, FillRule.NonZero, None)

        let resultArea = totalAbsArea result

        // 1% tolerance for rounding — same threshold as the clipper2-ts test.
        let tolerance = inputArea * 0.01
        Assert.IsTrue(
            resultArea <= inputArea + tolerance,
            sprintf "union area %f exceeds input area %f + tolerance %f" resultArea inputArea tolerance
        )
