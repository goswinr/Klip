#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"

#r "nuget: ResizeArrayT, 0.26.0"
#r "nuget: Fesher, 0.5.0"
#r "nuget: Euclid,0.30.1"

open System
open ResizeArrayT
open Fesher
open Euclid
open Klip
type rs = RhinoScriptSyntax
module R = ResizeArray

let input : ResizeArray<Polyline2D> =
    // resizeArray{
        // Polyline2D.create [| Pt(5, 5); Pt(7, 5); Pt(7, 7); Pt(4, 7); Pt(4, 6.262690989118976); Pt(5, 6.262690989118975); Pt(5, 5) |]
        // Polyline2D.create [| Pt(3, 4); Pt(5, 4); Pt(5, 6.262690989118975); Pt(3, 6.262690989118975); Pt(3, 4) |]
    // }
    resizeArray{
        Polyline2D.create [| Pt(5, 5); Pt(7, 5); Pt(7, 7); Pt(4, 7); Pt(4, 6.0000000001); Pt(5, 6.); Pt(5, 5) |]
        Polyline2D.create [| Pt(3, 4); Pt(5, 4); Pt(5, 6); Pt(3, 6); Pt(3, 4) |]
    }

let print (ps : ResizeArray<Polyline2D> ) =
    ps
    |> R.map Polyline2D.asFSharpCode
    |> R.iter (Printfn.gray "  %s")

let union(ps:Klip.Paths64<unit>) =
    let c = Clipper64()
    //c.ColinearityTolerance <- 1e-9
    c.AddPaths(Paths64.ensurePositiveOrientations ps, PathType.Subject)
    c.Execute(ClipType.Union, FillRule.NonZero) |> fst

let run() =
    if input.IsEmpty then failwith "input empty"
    let failCount = ref 0
    let successCount = ref 0
    for scf = -5 to 10 do
        let scale = 10.0 ** (float scf)
        Printfn.lightBlue $"Testing scale 1e{scf}.."
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

                    let res = union ps
                    if res.Count = 0 then
                        incr failCount
                        Printfn.red $"empty at sc {scale}, rot {rotm.InDegrees}:"
                        print xps
                    else
                        let r0 = res[0]
                        if r0.PointCount = 8 then
                            incr successCount
                            // Printfn.green $"OK at sc {sc}, rot {rotm.InDegrees}:"
                            // print ps
                        else
                            incr failCount
                            Printfn.red $"FailL: not 8 but {r0.PointCount} points at scale {scale}, rotation {rotm.InDegrees} moveX {moveX}, moveY {moveY}:"
                            //print xps


    Printfn.blue $"Done. Success: {successCount.Value}, Fail: {failCount.Value}"

run()































