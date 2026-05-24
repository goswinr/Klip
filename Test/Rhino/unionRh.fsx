#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "nuget: Clipper2, 2.0.0"
#r "nuget: Rhino.Scripting.FSharp"


#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"


open System
open System.IO
open System.Text.Json
open Klip
open Rhino.Scripting
open Rhino.Scripting.FSharp
open Rhino.Geometry

type rs = RhinoScriptSyntax

type XY = { x: float; y: float }

module Util =  

    let draw lay (ps: Point3d seq) =  
        ps
        |> Polyline
        |> fun p -> p.Add(p.First); p
        |> fun p -> p.RemoveNearlyEqualSubsequentPoints(1e-6); p
        |> fun p -> p.ToNurbsCurve() // nurbs allow duplicate points
        |> rs.Ot.AddCurve
        |> rs.setLayer lay 
    
    
    let drawXY (ps:XY[][]) =
        for j, p in Seq.indexed ps do
            try
                if p.Length > 2 then
                    p
                    |> Seq.map (fun xy -> Point3d(xy.x, xy.y, 0) )
                    |> draw "polysXY"
                else
                    rs.AddTextDot($"short", Point3d(p[0].x, p[1].y, 0) )
                    |> rs.setLayer "polysXY"
            with e ->
                eprintfn $"failed: polysXY with {p.Length} points"
                printfn $"{e}"
    
    
    
    let drawK sc (ps:Paths64<_>) =
        for j, p in Seq.indexed ps do
            try
                if p.PointCount > 2 then
                    p.XYs
                    |> Seq.chunkBySize 2
                    |> Seq.map (fun xy -> Point3d(xy[0], xy[1], 0) )
                    |> draw $"scale {sc}::Klip"
                else
                    rs.AddTextDot($"short", Point3d(p.XYs[0], p.XYs[1], 0) )
                    |> rs.setLayer $"scale {sc}::Klip"
            with e ->
                eprintfn $"failed: Klip::scale {sc}::{j} with {p.PointCount} points"
                printfn $"{e}"


    let drawC sc (ps:Clipper2Lib.PathsD) =
        for j, p in Seq.indexed ps do
            try
                if p.Count > 2 then
                    p
                    |> Seq.map (fun xy -> Point3d(xy.x, xy.y, 0) )
                    |> draw $"scale {sc}::Clipper2"
                else
                    rs.AddTextDot($"small", Point3d(p.[0].x, p.[1].y, 0) )
                    |> rs.setLayer $"scale {sc}::Clipper2"
            with e ->
                eprintfn $"failed: Clipper2 scale {sc}::{j} with {p.Count} points"
                printfn $"{e}"
                
    let convertD(xy:XY[][]) : Clipper2Lib.PathsD = 
        let ps = Clipper2Lib.PathsD()
        for xs in xy do
            let c = Clipper2Lib.PathD()
            for p in xs do
                c.Add(Clipper2Lib.PointD(p.x, p.y))
            ps.Add(c)
        ps

do
    rs.DisableRedraw()

    let xy: XY[][] =
        "D:/Git/_Euclid_/Klip/Test/Rhino/polysXY.json"
        |>  File.ReadAllText
        |>  JsonSerializer.Deserialize<XY[][][]>
        |>  Array.map Array.head
        |>! Util.drawXY
   

    printfn $"Original Paths: {xy.Length}"
    for i = 1 to 5 do
        let scale = 10. ** float i

        // Klip:
        let kr =
            xy
            |> Paths64.createFromxyMembers scale
            |> Klipper.unionSelf
            |> Paths64.scaleDown scale
        Util.drawK i kr
        printfn $"Klip:    Scale: {scale}, Result Paths: {kr.Count}"


        // Clipper2:
        let cr =
            Clipper2Lib.Clipper.BooleanOp(
                Clipper2Lib.ClipType.Union,
                Util.convertD xy, 
                null,
                Clipper2Lib.FillRule.EvenOdd,
                precision = i)
        Util.drawC i cr
        printfn $"Clipper2: Scale: {scale}, Result Paths: {cr.Count}\n-"
