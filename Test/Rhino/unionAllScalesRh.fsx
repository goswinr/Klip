#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"

#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: ResizeArrayT, 0.26.0"
#r "nuget: Fesher, 0.5.0"
#r "nuget: Euclid.Rhino,0.30.1"

open System
open Rhino.Scripting
open Rhino.Scripting.FSharp
open Rhino.Geometry
open ResizeArrayT
open Fesher
open Euclid
open Klip
type rs = RhinoScriptSyntax
module R = ResizeArray

let input : ResizeArray<Polyline2D> =
    // use with file: D:\Git\_Euclid_\Euclid\Test\TestInRhino\unionAtScale.3dm
    resizeArray{
        Polyline2D.create [| Pt(5, 5); Pt(7, 5); Pt(7, 7); Pt(4, 7); Pt(4, 6.262690989118976); Pt(5, 6.262690989118975); Pt(5, 5) |]
        Polyline2D.create [| Pt(3, 4); Pt(5, 4); Pt(5, 6.262690989118975); Pt(3, 6.262690989118975); Pt(3, 4) |]
    }
    // rs.ObjectsByLayer "Default"
    // |> R.map rs.CoercePolyline
    // |> ResizeArray.map Polyline2D.ofRhPolyline


let draw lay (ps:Klip.Paths64<unit>) =
    for p in ps do
        // Printfn.gray $"{p.PointCount} points on {lay}"
        p.XYs
        |> Seq.chunkBySize 2
        |> Seq.map (fun xy -> Point3d(xy[0], xy[1], 0) )
        |> Polyline
        |> fun p -> p.Add(p.First); p
        // |> fun p -> p.RemoveNearlyEqualSubsequentPoints(1e-6); p
        |> fun p -> p.ToNurbsCurve() // nurbs allow duplicate points
        |> rs.Ot.AddCurve
        |> rs.setLayer $"draw::{lay}"

let print (ps : ResizeArray<Polyline2D> ) =
    ps
    |> R.map Polyline2D.asFSharpCode
    |> R.iter (Printfn.gray "  %s")

let areaMatch (pas:Paths64<_>)  (pbs:Paths64<_>)=
    let a = pas |> Seq.sumBy _.AbsArea
    let b = pbs |> Seq.sumBy _.AbsArea
    abs(a-b) < ( max a b) * 0.01 // max 1% diffrence


let doUnion tol (ps:Klip.Paths64<unit>) =
    let c = Clipper64()
    // c.ColinearityTolerance <- 1e-9
    // c.MergeVertexTolerance <- 1e-9
    // c.SnapXandYTolerance <- 1e-6
    // c.SnapXandY <- false
    c.AddPaths(Paths64.ensurePositiveOrientations ps, PathType.Subject)
    c,  c.Execute(ClipType.Union, FillRule.NonZero) |> fst

let run() =
    if input.IsEmpty then failwith "input empty"
    let failCount = ref 0
    let successCount = ref 0
    Printfn.blue "Input:"
    print input
    for scf = -5 to 10 do
        let scale = 10.0 ** (float scf)
        Printfn.blue $"Testing scale 1e{scf}.."
        for moveX in [0.0; 137.9; -1235.0] do
            // for moveY in [0.0; 31.3; 299.;  1000.0] do
                // for rot in [0.;0.01; 0.02; 45.; 79.99; 90.; 90.01;  115; 179.999;  180.;180.0001; 270.; 360. ] do
            for moveY in [0.0; 31.3; 1000.0] do
                for rot in [0.;0.01; 45.; 90.; 90.01;  115; 179.999;  180.;180.0001; 270.; 360. ] do
                    let rotm = Rotation2D.createFromDegrees rot
                    let xps =
                        input
                        |> ResizeArray.map (
                                Polyline2D.moveX moveX
                                >> Polyline2D.moveY moveY
                                >> Polyline2D.scale scale
                                >> Polyline2D.rotate rotm
                                )
                    let ps =
                        xps
                        |>  R.map Polyline2D.asPoints
                        |>  Paths64.createFromXYMembers

                    let c, res = doUnion (scale * 1e-6)  ps
                    if res.Count = 0 then
                        incr failCount
                        Printfn.red $"Empty at sc {scale}, rot {rotm.InDegrees}"
                    else
                        let r0 = res[0]
                        if r0.PointCount = 8 && areaMatch res ps then
                            incr successCount
                            // Printfn.green $"OK at sc {sc}, rot {rotm.InDegrees}:"
                            // print ps
                            // draw $"sc {sc}, rot {rotm.InDegrees}:" r
                        else
                            incr failCount
                            Printfn.red $"Fail: not 8 but {r0.PointCount} points at scale {scale}, rotation {rotm.InDegrees} moveX {moveX}, moveY {moveY}:"
                            Printfn.gray $"Klip: ColinearityTolerance {c.ColinearityTolerance}, MergeVertexTolerance {c.MergeVertexTolerance}"

                            // print xps
                            // draw $"sc {scale}, rot {rotm.InDegrees}" res
                            // draw $"sc {scale}, rot {rotm.InDegrees} -input" ps

    Printfn.blue $"Done. Success: {successCount.Value}, Fail: {failCount.Value}"


run()































