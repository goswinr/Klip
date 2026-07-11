#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"
#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: Fesher, 0.5.0"
#r "nuget: Euclid.Rhino,0.30.1"
// #r "nuget: Clipper2, 2.0.0"
//
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


let draw lay (ps:Klip.Paths64<unit>) =
    for p in ps do
        p.XYs
        |> Polyline2D.createDirectly
        |> Polyline2D.close 1e-5
        |> Polyline2D.toRhPolylineCurve
        |> rs.Ot.AddCurve
        |> rs.setLayer $"draw::{lay}"


let input : seq<Polyline2D> = 
    rs.GetObjects()
    |> Seq.map rs.CoercePolyline
    |> Seq.map Polyline2D.ofRhPolyline
    

let pathK =
    input
    |>  Seq.map Polyline2D.asPoints
    |>  Paths64.createFromXYMembers
    // |>! Snap.xAndY 0.001 // try to make it pass without

let res = unionK (1e-5) pathK 
draw "res" res



























































