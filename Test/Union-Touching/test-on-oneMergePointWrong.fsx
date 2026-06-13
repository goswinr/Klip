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


let inputOrig = // OK 
    ResizeArray [
        Polyline2D.create [|
            Pt(-50000, 50000)
            Pt(-49999.99999999999, 70000)
            Pt(-70000, 70000)
            Pt(-70000, 40000.00000000001)
            Pt(-62626.90989118976, 40000.00000000001)
            Pt(-62626.90989118975, 50000.00000000001)
            // Pt(-50000, 50000) // fails without this
            |]
        Polyline2D.create [|
            Pt(-40000, 30000.000000000004)
            Pt(-40000, 50000)
            Pt(-62626.90989118975, 50000.00000000001)
            Pt(-62626.90989118975, 30000.000000000004)
            // Pt(-40000, 30000.000000000004) // fails without this
            |]
    ]

let input  = // OK: inputOK1 scaled down
    ResizeArray [
        Polyline2D.create [|
            Pt(-50, 50)
            Pt(-40, 70)
            Pt(-70, 70)
            Pt(-70, 40)
            Pt(-62, 40)
            Pt(-62, 50.00000000000001)
            |]
        Polyline2D.create [|
            Pt(-40, 30)
            Pt(-40, 50)
            Pt(-62, 50.00000000000001)
            Pt(-62, 30)
            |]
    ]
   
let printGeo (ps:seq<Polyline2D> ) =
    ps
    |> Seq.map Polyline2D.asFSharpCode
    |> Seq.iter (Printfn.gray "  %s")

let printK (ps:Paths64<_> ) =
    ps
    |> Seq.map (_.XYs >> Polyline2D.createDirectly)
    |> printGeo

let unionK (ps:Klip.Paths64<unit>) =
    let c = Clipper64()
    // c.MergeVertexTolerance <- 1e-6  // should be smaller than 1e-5
    // c.ColinearityTolerance <- 1e4 // at 1e-4 or bigger
    c.CoordEqTolerance <- 1e-2 // needs to be adjuated to scale !!
    //
    // c.NearTopYToleranceCap <- 1e-3
    // c.NearTopYToleranceFactor <- 1e-6
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

let run(shift) =
    
    let inputHalfSnaped = ResizeArray [ // Fails
        Polyline2D.create [|  
            Pt(-50, 50);
            Pt(-50, 70) 
            Pt(-70, 70)
            Pt(-70, 40)
            Pt(-62, 40)
            Pt(-62, 50.0+shift) |]
        Polyline2D.create [|  
            Pt(-40, 30)
            Pt(-40, 50)
            Pt(-62, 50)
            Pt(-62, 30) 
            |]
        ]
    
    let pathK =
        inputHalfSnaped
        |>  Seq.map Polyline2D.asPoints
        |>  Paths64.createFromXYMembers
        // |>! Snap.xAndY 0.0001 // not needed anymore
    
    // printK pathK
    let res = unionK pathK
    // draw $"{shift}" res

    if res.Count = 1 && res[0].PointCount = 8 && areaOkK pathK res then
        Printfn.green $"SUCCESS"
    elif res.Count = 0 then
        Printfn.red "ERROR empty path"
    else
        Printfn.red $"ERROR: {res.Count} paths, first has {res[0].PointCount} points, area ok: {areaOkK pathK res}"

for f = -9 to -1 do 
    let shift = 10.0 ** (float f)
    printf $"{shift}: "
    run shift
































