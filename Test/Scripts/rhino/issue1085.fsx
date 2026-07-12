#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "../../../bin/Release/netstandard2.0/Klip.dll"

#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: Euclid.Rhino,0.30.1"

open Euclid
open Klip
open Rhino.Scripting.FSharp
open Rhino.Scripting

type rs = RhinoScriptSyntax


// same behaviour here as in the original issue

// Reproduction of https://github.com/AngusJohnson/Clipper2/issues/1085
// Union of 4 paths (NonZero). The first union produces a single path with a
// zero-width bridge connecting two regions that should be separate; a second
// union splits them into the expected two paths.



let input : ResizeArray<Polyline2D> =
    ResizeArray[
        Polyline2D.create [
            Pt( 420.0, -270.0)
            Pt( 500.0,    0.0)
            Pt( 470.0,  100.0)
            Pt(   0.0,  100.0)
            Pt(   0.0, -483.0)
            Pt( 207.0, -454.0)
        ]

        Polyline2D.create [
            Pt(   0.0,  100.0)
            Pt( 370.0,  100.0)
            Pt( 400.0,    0.0)
            Pt( 336.0, -216.0)
            Pt( 166.0, -363.0)
            Pt(   0.0, -386.0)
        ]

        Polyline2D.create [
            Pt( 252.0, -162.0)
            Pt( 300.0,    0.0)
            Pt( 270.0,  100.0)
            Pt(   0.0,  100.0)
            Pt(   0.0, -289.0)
            Pt( 124.0, -272.0)
        ]

        Polyline2D.create [
            Pt(   0.0,  100.0)
            Pt( 170.0,  100.0)
            Pt( 200.0,    0.0)
            Pt( 168.0, -108.0)
            Pt(  83.0, -181.0)
            Pt(   0.0, -192.0)
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
    c.ColinearityTolerance <- 1e-4
    c.MergeVertexTolerance <- 1.
    c.AddPaths(ps, PathType.Subject) // no positve direction ensured here
    c.Execute(ClipType.Union, FillRule.NonZero) |> fst

let inputPaths =
    input
    |> Seq.map Polyline2D.asPoints
    |> Paths64.createFromXYMembers

printfn $"Input: {input.Count} paths"
draw "input" inputPaths

let resKlip = inputPaths |> unionKlip

printfn $"Result (1st union): {resKlip.Count} paths"
for p in resKlip do printfn $"  {p.XYs.Count} points"
draw "result1" resKlip

// second union, as in the issue's workaround
let resKlip2 = resKlip |> unionKlip

printfn $"Result (2nd union): {resKlip2.Count} paths"
for p in resKlip2 do printfn $"  {p.XYs.Count} points"
draw "result2" resKlip2
