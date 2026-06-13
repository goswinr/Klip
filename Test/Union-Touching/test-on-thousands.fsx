#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"
#r "nuget: Fesher, 0.5.0"
#r "nuget: Euclid,0.30.1"
#r "nuget: Clipper2, 2.0.0"

open System
open Fesher
open Euclid
open Klip

let print (ps : seq<Polyline2D> ) =
    Printfn.gray  "ResizeArray ["
    ps
    |> Seq.map Polyline2D.asFSharpCode
    |> Seq.iter (Printfn.gray "    %s")
    Printfn.gray  "    ]"

let printK (ps : Paths64<_> ) =
    ps
    |> Seq.map (_.XYs >> Polyline2D.createDirectly)
    |> print

let unionK (scale:float)  (ps:Klip.Paths64<unit>) =
    let c = Clipper64()

    c.MergeVertexTolerance <- 1e-6  // should be smaller than 1e-5
    c.ColinearityTolerance <- 1e-2 // at 1e-4 or bigger
    c.CoordEqTolerance <-  1e-3 * scale // should be bigger than 1e-4,  need scale factor

    // c.NearTopYToleranceCap <- 1e-3
    // c.NearTopYToleranceFactor <- 1e-6

    c.AddPaths(ps, PathType.Subject)
    c.Execute(ClipType.Union, FillRule.NonZero) |> fst

let unionC (precision:int) (ps:Clipper2Lib.PathsD) =
    Clipper2Lib.Clipper.Union(ps, null,  Clipper2Lib.FillRule.EvenOdd, precision = precision)


let areaOkC (a:Clipper2Lib.PathsD)  (b:Clipper2Lib.PathsD) =
    let aa = a |> Seq.sumBy (Clipper2Lib.Clipper.Area>>abs)
    let bb = b |> Seq.sumBy (Clipper2Lib.Clipper.Area>>abs)
    abs(aa-bb) < (max aa bb) * 0.01 // within 1 %

let areaOkK (a:Klip.Paths64<_>)  (b:Klip.Paths64<_>) =
    let aa = a |> Seq.sumBy Klip.Path64.absArea
    let bb = b |> Seq.sumBy Klip.Path64.absArea
    abs(aa-bb) < (max aa bb) * 0.01 // within 1 %


let failCount = ref 0
let successCount = ref 0
let thisScaleFailCount = ref 0

let logK (res: Klip.Paths64<_>, shift, moveX:float, moveY:float, scale:float, clipperPrec:int, rot:float, pathK: Klip.Paths64<_>) =
    if res.Count = 0 then
        incr failCount
        Printfn.purple $"  Empty Result at scale {scale}, rotation {rot} moveX {moveX}, moveY {moveY}"
        // print xps
    else
        let r0 = res[0]
        if r0.PointCount = 8 && areaOkK res pathK then
            incr successCount
            // Printfn.green $"OK at sc {sc}, rot {rotm.InDegrees}:"
            // print ps
        elif not <| areaOkK res pathK then
            incr failCount
            incr thisScaleFailCount
            if !thisScaleFailCount < 3 then
                Printfn.purple $"  BAD AREA: {r0.PointCount} points at scale {scale}, rotation {rot} moveX {moveX}, moveY {moveY}"
        else
            incr failCount
            incr thisScaleFailCount
            if !thisScaleFailCount < 3 then
                Printfn.red $"  BAD COUNT: {r0.PointCount} transformAndRun ({shift}, {moveX}, {moveY}, {scale}, {clipperPrec}, {rot})"
                //print xps

let logC (res: Clipper2Lib.PathsD, shift, moveX:float, moveY:float, scale:float, clipperPrec:int, rot:float, pathC: Clipper2Lib.PathsD) =
    if res.Count = 0 then
        incr failCount
        Printfn.purple $"  Empty Result at scale {scale}, rotation {rot} moveX {moveX}, moveY {moveY}"
        // print xps
    else
        let r0 = res[0]
        if r0.Count = 8 && areaOkC res pathC then
            incr successCount
            // Printfn.green $"OK at sc {sc}, rot {rotm.InDegrees}:"
            // print ps
        elif not <| areaOkC res pathC then
            incr failCount
            incr thisScaleFailCount
            if !thisScaleFailCount < 3 then
                Printfn.purple $"  BAD AREA: {r0.Count} points at scale {scale}, rotation {rot} moveX {moveX}, moveY {moveY}"
        else
            incr failCount
            incr thisScaleFailCount
            if !thisScaleFailCount < 3 then
                Printfn.red $"  BAD COUNT: {r0.Count} points at scale {scale}, rotation {rot} moveX {moveX}, moveY {moveY}"
                //print xps

let transformAndRun (shift, moveX:float, moveY:float, scale:float, clipperPrec:int, rot:float) =

    let ensureCounterClockwise (pl:Polyline2D) : Polyline2D =
        if pl.IsCounterClockwise then pl else pl.Reverse()

    let input : ResizeArray<Polyline2D> =
        ResizeArray[
            Polyline2D.create [| Pt(5, 5); Pt(7, 5); Pt(7, 7); Pt(4, 7); Pt(4, 6.0 + shift ); Pt(5, 6); Pt(5, 5) |]|> ensureCounterClockwise
            Polyline2D.create [| Pt(3, 4); Pt(5, 4); Pt(5, 6); Pt(3, 6); Pt(3, 4) |] |> ensureCounterClockwise
        ]

    let rotm = Rotation2D.createFromDegrees rot
    let transformed : seq<Polyline2D>=
        input
        |> Seq.map (
                Polyline2D.moveX moveX
                >> Polyline2D.moveY moveY
                >> Polyline2D.scale scale
                >> Polyline2D.rotate rotm
                )
    // Klip:
    let pathK =
        transformed
        |>  Seq.map Polyline2D.asPoints
        |>  Paths64.createFromXYMembers
    let resK = unionK scale pathK
    // let resK = unionK scale resK
    logK (resK, shift, moveX, moveY, scale, clipperPrec, rot, pathK)
    
    //Clipper2:
    // let pathC : Clipper2Lib.PathsD =
        // let ps = Clipper2Lib.PathsD()
        // for xs in transformed do
            // let c = Clipper2Lib.PathD()
            // for p in xs.AsPoints do
                // c.Add(Clipper2Lib.PointD(p.X, p.Y))
            // ps.Add(c)
        // ps
    // let resC = unionC (clipperPrec-2) pathC
    // logC (resC, shift, moveX, moveY, scale, clipperPrec, rot, pathC)
    ()

let run() =
    for sign in [-1.0; 1.0] do
        for shift0 in [0.0 ; 1e-3; 1e-5; 1e-7; 1e-7] do
            let shift = shift0 * sign
            Printfn.blue $"Testing shift {shift}.."
            for precision = -4 to 7 do
                let clipperPrec = 3 - precision
                let scale = 10.0 ** (float precision)
                Printfn.lightBlue $" Testing scale 1e{precision},  Clipper2 {clipperPrec} .."
                for moveX in [0.0; 137.9; -1235.0] do
                    for moveY in [0.0; 31.3; 1000.0] do
                        for rot in [0.; 0.0001; 0.01; 45.; 79.999; 90.; 90.01;  115; 179.999;  180.;180.0001; 270.; 360. ] do
                            transformAndRun (shift, moveX, moveY, scale, clipperPrec,rot)

                if !thisScaleFailCount > 4 then
                    Printfn.red $"   .. and {!thisScaleFailCount - 2} more"
                thisScaleFailCount.Value <- 0
    Printfn.blue $"Done. Success: {successCount.Value}"
    Printfn.red $"Fail: {failCount.Value}"

run()






























