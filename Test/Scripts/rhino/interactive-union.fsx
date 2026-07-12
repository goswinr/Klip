#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "../../../bin/Release/netstandard2.0/Klip.dll"

#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: Euclid.Rhino,0.30.1"

open Euclid
open Klip
open Rhino.Scripting.FSharp
open Rhino.Scripting

type rs = RhinoScriptSyntax

rs.DisableRedraw()
// rs.LayerOn "poly"

let input : ResizeArray<Polyline2D> =
    // rs.GetObjects "polygons"
    rs.GetObjectsAndRemember "select polygons"
    // |>! Seq.iter (rs.HideObject >> ignore) 
    |>  Seq.map rs.CoercePolyline
    |>  Seq.map Polyline2D.ofRhPolyline
    |>  ResizeArray

let draw lay (ps:Klip.Paths64<unit>) =
    for p in ps do
        p.XYs
        |> Polyline2D.createDirectly
        |> Polyline2D.close 1e-6
        |> Polyline2D.toRhPolylineCurve
        |> rs.Ot.AddCurve
        |> rs.setLayer $"draw::{lay}"

// prints one point per line, so the output can be pasted back as a repro fixture
let printAsCode(ps:ResizeArray<Polyline2D> ) =
    ps
    |> Seq.map Polyline2D.asFSharpCode
    |> Seq.map ( fun s -> s.Replace("; ", "\n      "))
    |> Seq.map ( fun s -> s.Replace("[|", "[|\n     "))
    |> Seq.map ( fun s -> s.Replace("|]", "\n  |]"))
    |> Seq.iter (printfn "  %s")

let unionKlip(ps:Klip.Paths64<unit>) =
    let c = Clipper64()
    c.Tolerance <- 2.0
    c.ColinearityTolerance <- 0.1
    c.AddPaths(Paths64.ensurePositiveOrientations ps, PathType.Subject)
    c.Execute(ClipType.Union, FillRule.NonZero) |> fst

let inputPaths =
    input
    |>! printAsCode
    |>  Seq.map Polyline2D.asPoints
    |>  Paths64.createFromXYMembers

printfn $"Input: {input.Count} paths"
// draw $"input-{input.Count}" inputPaths
let res1 = inputPaths |> unionKlip
printfn $"Result1: {res1.Count} paths"
draw $"result-{input.Count}" res1

// repeat to clean up ?
// let res2 = res1 |> unionKlip
// printfn $"Result2: {res2.Count} paths"
// draw $"result2-{input.Count}" res2

// for p in r do
    // let pl = Polyline2D.createDirectly p.XYs
    // printfn $"{pl.AsFSharpCode}"

rs.LayerOff "poly"
rs.EnableRedraw()


(*
Polyline2D.create [| Pt(9, 35); Pt(9, 37); Pt(7, 37.00000000000001); Pt(7, 34); Pt(14, 34); Pt(14, 37.00000000000001); Pt(12, 37); Pt(12, 35); Pt(9, 35) |]
Polyline2D.create [| Pt(7, 37.00000000000001); Pt(8, 37); Pt(8, 38); Pt(7, 38); Pt(7, 37.00000000000001) |]
*)























