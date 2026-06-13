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


let print (ps : ResizeArray<Polyline2D> ) =
    Printfn.gray  "ResizeArray ["
    ps
    |> R.map Polyline2D.asFSharpCode
    |> R.iter (Printfn.gray "    %s")
    Printfn.gray  "    ]"

let printK (ps : Paths64<_> ) =
    ps
    |> R.map (_.XYs >> Polyline2D.createDirectly)
    |> print

let union (scale:float)  (ps:Klip.Paths64<unit>) =
    let c = Clipper64()

    c.MergeVertexTolerance <- scale * 1e-5
    c.ColinearityTolerance <- 1e-7 // at 1-e7
    // c.NearTopYToleranceCap <- 1e-3
    // c.NearTopYToleranceFactor <- 1e-6
    c.CoordEqTolerance <- scale * 1e-5

    c.AddPaths(Paths64.ensurePositiveOrientations ps, PathType.Subject)
    c.Execute(ClipType.Union, FillRule.NonZero) |> fst


let areaOk (a:Paths64<_>)  (b:Paths64<_>) =
    let aa = a |> Seq.sumBy Klip.Path64.absArea
    let bb = b |> Seq.sumBy Klip.Path64.absArea
    abs(aa-bb) < (max aa bb) * 0.01 // within 1 %


let failCount = ref 0
let successCount = ref 0
let thisScaleFailCount = ref 0
let transformAndRun (shift, moveX:float, moveY:float, scale:float, rot:float) =

    let input : ResizeArray<Polyline2D> =
        // resizeArray{
            // Polyline2D.create [| Pt(5, 5); Pt(7, 5); Pt(7, 7); Pt(4, 7); Pt(4, 6.262690989118976); Pt(5, 6.262690989118975); Pt(5, 5) |]
            // Polyline2D.create [| Pt(3, 4); Pt(5, 4); Pt(5, 6.262690989118975); Pt(3, 6.262690989118975); Pt(3, 4) |]
        // }
        resizeArray{
            Polyline2D.create [| Pt(5, 5); Pt(7, 5); Pt(7, 7); Pt(4, 7); Pt(4, 6.0 + shift ); Pt(5, 6.); Pt(5, 5) |]
            Polyline2D.create [| Pt(3, 4); Pt(5, 4); Pt(5, 6); Pt(3, 6); Pt(3, 4) |]
        }

    let rotm = Rotation2D.createFromDegrees rot
    let xps : ResizeArray<Polyline2D>=
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

    // printK ps
    let res = union scale ps
    if res.Count = 0 then
        incr failCount
        Printfn.purple $"  Empty Result at scale {scale}, rotation {rotm.InDegrees} moveX {moveX}, moveY {moveY}"
        // print xps
    else
        let r0 = res[0]
        if r0.PointCount = 8 && areaOk res ps then
            incr successCount
            // Printfn.green $"OK at sc {sc}, rot {rotm.InDegrees}:"
            // print ps
        elif not <| areaOk res ps then
            incr failCount
            incr thisScaleFailCount
            if !thisScaleFailCount < 3 then
                Printfn.purple $"  BAD AREA{r0.PointCount} points at scale {scale}, rotation {rotm.InDegrees} moveX {moveX}, moveY {moveY}"
        else
            incr failCount
            incr thisScaleFailCount
            if !thisScaleFailCount < 3 then
                Printfn.red $"  BAD COUNT: {r0.PointCount} points at scale {scale}, rotation {rotm.InDegrees} moveX {moveX}, moveY {moveY}"
                //print xps

let run() =
    for shift in [0.0 ; 0.0000001; 0.000000001;] do
        Printfn.blue $"Testing shift {shift}.."
        for scf = -4 to 7 do
            let scale = 10.0 ** (float scf)
            Printfn.lightBlue $" Testing scale 1e{scf}.."
            for moveX in [0.0; 137.9; -1235.0] do
                for moveY in [0.0; 31.3; 1000.0] do
                    for rot in [0.; 0.0001; 0.01; 45.; 79.999; 90.; 90.01;  115; 179.999;  180.;180.0001; 270.; 360. ] do
                        transformAndRun (shift, moveX, moveY, scale, rot)

            if !thisScaleFailCount > 4 then
                Printfn.red $"   .. amd {!thisScaleFailCount - 2} more"
            thisScaleFailCount.Value <- 0
    Printfn.blue $"Done. Success: {successCount.Value}"
    Printfn.red $"Fail: {failCount.Value}"

run()
// transformAndRun (0., 0., 1.0,  270)





























