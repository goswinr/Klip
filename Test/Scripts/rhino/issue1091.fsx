#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "../../../bin/Release/netstandard2.0/Klip.dll"

#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: Euclid.Rhino,0.30.1"

open Euclid
open Klip
open Rhino.Scripting.FSharp
open Rhino.Scripting

type rs = RhinoScriptSyntax

let input : ResizeArray<Polyline2D> =
    ResizeArray[
        Polyline2D.create [
            Pt(0.00049999,  1.0)
            Pt(0.00049999, -1.0)
            Pt(-3.0,  0.0)
        ]

        Polyline2D.create [
            Pt(0.00050001,  1.0)
            Pt(0.00050001, -1.0)
            Pt( 3.0,  0.0)
        ]
    ]

let draw lay (ps:Klip.Paths64<unit>) =
    for p in ps do
        p.XYs
        |> Polyline2D.createDirectly
        |> Polyline2D.close 1e-6
        |> Polyline2D.toRhPolylineCurve
        |> rs.Ot.AddCurve
        |> rs.setLayer $"draw::{lay}"

let print (ps:ResizeArray<Polyline2D> ) =
    ps
    |> Seq.map Polyline2D.asFSharpCode
    |> Seq.iter (printfn "  %s")

let unionKlip(ps:Klip.Paths64<unit>) =
    let c = Clipper64()
    // c.ColinearityTolerance <- 1e-8
    // c.MergeVertexTolerance <- 0.002
    c.AddPaths(Paths64.ensurePositiveOrientations ps, PathType.Subject)
    c.Execute(ClipType.Union, FillRule.NonZero) |> fst

let inputPaths =
    input
    |> Seq.map Polyline2D.asPoints
    |> Paths64.createFromXYMembers

printfn $"Input: {input.Count} paths"
draw "input" inputPaths

let resKlip = inputPaths |> unionKlip

printfn $"Result: {resKlip.Count} paths"
draw "result" resKlip


// for p in r do
    // let pl = Polyline2D.createDirectly p.XYs
    // printfn $"{pl.AsFSharpCode}"





























