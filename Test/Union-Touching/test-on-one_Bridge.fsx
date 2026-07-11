#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"
#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: Fesher, 0.5.0"
#r "nuget: Euclid.Rhino,0.30.1"
#r "nuget: Clipper2, 2.0.0"
open Fesher
open Euclid
open Klip
open Rhino.Scripting
open Rhino.Scripting.FSharp
type rs = RhinoScriptSyntax

let printGeo (ps:seq<Polyline2D> ) =
    ps
    |> Seq.map Polyline2D.asFSharpCode
    |> Seq.map ( fun s -> s.Replace("; ", "\n      ")) 
    |> Seq.map ( fun s -> s.Replace("[|", "[|\n     ")) 
    |> Seq.map ( fun s -> s.Replace("|]", "\n  |]")) 
    |> Seq.iter (Printfn.gray "  %s")

let printK (ps:Paths64<_> ) =
    ps
    |> Seq.map (_.XYs >> Polyline2D.createDirectly)
    |> printGeo

/// Largest absolute coordinate in the input - a proxy for the geometry's scale.
let charSize (ps:Klip.Paths64<unit>) =
    let mutable m = 0.0
    for p in ps do
        for v in p.XYs do
            let a = abs v
            if a > m then m <- a
    m

let unionK tol (ps:Klip.Paths64<unit>) =
    let c = Clipper64()
    // CoordEqTolerance and MergeVertexTolerance are absolute *distances*, so they must scale with
    // the coordinate magnitude. A relative factor anywhere in [1e-7 .. 5e-7] makes the whole
    // shift/rot sweep pass at every scale (0.001 .. 1e7); 2.6e-7 reproduces the legacy 1e-5 at the
    // original unit-scale geometry. The other knobs are dimensionless (slope/angle ratios), so they
    // stay fixed across scale.
    // let tol = 2.6e-7 * charSize ps
    c.CoordEqTolerance <- tol
    c.MergeVertexTolerance <- tol
    
    // c.ColinearityTolerance <- 1e-3   // dimensionless (sin θ) - scale-independent default is fine
    // c.HorizontalAngleTolerance <- 1e-6 // dimensionless slope - scale-independent default is fine
    c.AddPaths(ps, PathType.Subject)
    c.Execute(ClipType.Union, FillRule.NonZero) |> fst


let unionC (precision:int) (ps:Clipper2Lib.PathsD) =
    Clipper2Lib.Clipper.Union(ps, null,  Clipper2Lib.FillRule.NonZero, precision = precision)


let areaOkC (a:Clipper2Lib.PathsD)  (b:Clipper2Lib.PathsD) =
    let aa = a |> Seq.sumBy (Clipper2Lib.Clipper.Area>>abs)
    let bb = b |> Seq.sumBy (Clipper2Lib.Clipper.Area>>abs)
    abs(aa-bb) < (max aa bb) * 0.01 // within 1 %

let areaOkK (a:Klip.Paths64<_>)  (b:Klip.Paths64<_>) =
    let aa = a |> Seq.sumBy Klip.Path64.absArea
    let bb = b |> Seq.sumBy Klip.Path64.absArea
    abs(aa-bb) < (max aa bb) * 0.01 // within 1 %

let draw lay (ps:Klip.Paths64<unit>) =
    for p in ps do
        p.XYs
        |> Polyline2D.createDirectly
        |> Polyline2D.close 1e-5
        |> Polyline2D.toRhPolylineCurve
        |> rs.Ot.AddCurve
        |> rs.setLayer $"draw::{lay}"

let OKk = ref 0
let NotOKk = ref 0
let run shift rot scale =
    let input : ResizeArray<Polyline2D> =
        ResizeArray [
            Polyline2D.create [|
                Pt(9.0,  35.0)
                Pt(9.0,  37.0)
                Pt(7.0,  37.0 + shift)
                Pt(7.0,  34.0)
                Pt(14.0, 34.0)
                Pt(14.0, 37.0 + shift)
                Pt(12.0, 37.0)
                Pt(12.0, 35.0) 
                |]  
                |> Polyline2D.scale scale
                |> Polyline2D.rotate (Rotation2D.createFromDegrees rot) 
            Polyline2D.create [|
                Pt(7.0,  37.0 + shift)
                Pt(8.0,  37.0)
                Pt(8.0,  38.0)
                Pt(7.0,  38.0) // or us this to make sure shift at start also works
                |] 
                |> Polyline2D.scale scale
                |> Polyline2D.rotate (Rotation2D.createFromDegrees rot)
        ]

    let pathK =
        input
        |>  Seq.map Polyline2D.asPoints
        |>  Paths64.createFromXYMembers
        // |>! Snap.xAndY 0.001 // try to make it pass without

    let res = unionK (scale* 1e-5) pathK 

    if res.Count = 1 && res[0].PointCount = 11 && areaOkK pathK res then
        incr OKk
        // Printfn.green $"SUCCESS,scale {scale} shift:{shift},  rot {rot}"
        
    elif res.Count = 0 then
        incr NotOKk
        Printfn.orange $"ERROR, scale {scale} shift:{shift}, rot {rot}, empty path"
    else
        incr NotOKk
        Printfn.red $"ERROR,scale {scale} shift:{shift}, rot {rot}: {res.Count} paths, first has {res[0].PointCount} points, area ok: {areaOkK pathK res}" 
        // draw $"shift {shift} rot {rot}" res
        // printK pathK
        //
for scale in [0.1; 1.0; 100.0; 1000.0; 10000.0] do
    for shiftf = -9 to -2 do 
        for rot in [0.0;  0.0001 ;0.001 ;0.01 ;0.1 ; 45.;  90.;  180.; ] do 
            let shift = 10.0 ** (float shiftf) 
            run shift rot scale
Printfn.darkGreen $"{OKk.Value}x SUCCESS"
Printfn.darkRed $"{NotOKk.Value}x ERROR"




























































