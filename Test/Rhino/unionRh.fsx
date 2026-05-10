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

do
    rs.DisableRedraw()

    let xy: XY[][][] =
        "D:/Git/_Euclid_/Klip/Test/Rhino/polysXY.json"
        |> File.ReadAllText
        |> JsonSerializer.Deserialize<_>

    let xy: XY[][] =
        xy
        |> Array.map Array.head


    let drawK sc (ps:Paths64<_>) =
        for j, p in Seq.indexed ps do
            try
                if p.PointCount > 2 then
                    p.XYs
                    |> Seq.chunkBySize 2
                    |> Seq.map (fun xy -> Point3d(xy[0], xy[1], 0) )
                    |> Polyline
                    |> fun p -> p.ToNurbsCurve() // nurbs allow duplicate points
                    |> rs.Ot.AddCurve
                    |> rs.setLayer $"scale {sc}::Klip"
                else
                    rs.AddTextDot($"short", Point3d(p.XYs[0], p.XYs[1], 0) )
                    |> rs.setLayer $"scale {sc}::Klip"
            with e ->
                eprintfn $"failed: Klip::scale {sc}::{j} with {p.PointCount} points"
                printfn $"{e}"


    let drawC sc (ps:Clipper2Lib.PathsD) =
        for j, p in Seq.indexed ps do
            try
                if p.Count > 3 then
                    p
                    |> Seq.map (fun xy -> Point3d(xy.x, xy.y, 0) )
                    |> Polyline
                    |> fun p -> p.ToNurbsCurve() // nurbs allow duplicate points
                    |> rs.Ot.AddCurve
                    |> rs.setLayer $"scale {sc}::Clipper2"
                else
                    rs.AddTextDot($"small", Point3d(p.[0].x, p.[1].y, 0) )
                    |> rs.setLayer $"scale {sc}::Clipper2"
            with e ->
                eprintfn $"failed: Clipper2 scale {sc}::{j} with {p.Count} points"
                printfn $"{e}"


    printfn $"Original Paths: {xy.Length}"
    for i = 1 to 4 do
        let scale = 10. ** float i

        // Klip:
        let kr =
            xy
            |> Paths64.createFromxyMembers scale
            |> Clipper.unionSelf
            |> Paths64.scaleDown scale

        drawK i kr
        printfn $"Klip:    Scale: {scale}, Result Paths: {kr.Count}"

        // Clipper2:
        let cD =
            let ps = Clipper2Lib.PathsD()
            for xs in xy do
                let c = Clipper2Lib.PathD()
                for p in xs do
                    c.Add(Clipper2Lib.PointD(p.x, p.y))
                ps.Add(c)
            ps

        let cr =
            Clipper2Lib.Clipper.BooleanOp(
                Clipper2Lib.ClipType.Union,
                cD, null,
                Clipper2Lib.FillRule.EvenOdd,
                precision = i)

        drawC i cr
        printfn $"Clipper: Scale: {scale}, Result Paths: {cr.Count}\n-"