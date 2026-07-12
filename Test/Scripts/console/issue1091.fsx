#r "../../../bin/Release/netstandard2.0/Klip.dll"

#r "nuget: ResizeArrayT, 0.26.0"
#r "nuget: Euclid,0.30.1"

// https://github.com/AngusJohnson/Clipper2/issues/1091

open Euclid
open Klip


let a = Polyline2D.create [
    Pt(0.00049999,  1.0) // Point a0
    Pt(0.00049999, -1.0) // Point a1
    Pt(-3.0,  0.0)
    ]

let b = Polyline2D.create [
    Pt(0.00050001,  1.0) // Point b0 only has a distance of 0.00000002 from a0
    Pt(0.00050001, -1.0) // Point b1 only has a distance of 0.00000002 from a1
    Pt( 3.0,  0.0)
    ]

let input : ResizeArray<Polyline2D> =
    ResizeArray[a;b]

let union(ps:Klip.Paths64<unit>) =
    let c = Clipper64()
    c.ColinearityTolerance <- 1e-8
    // c.MergeVertexTolerance <- 0.002
    c.AddPaths(Paths64.ensurePositiveOrientations ps, PathType.Subject)
    c.Execute(ClipType.Union, FillRule.NonZero) |> fst

let r : Paths64<unit> =
    input
    |> Seq.map Polyline2D.asPoints
    |> Paths64.createFromXYMembers
    |> union

printfn $"Union result: {r.Count} paths"
// for p in r do
    // let pl = Polyline2D.createDirectly p.XYs
    // printfn $"{pl.AsFSharpCode}"





