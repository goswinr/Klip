#r "nuget: Clipper2, 2.0.0"
#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"

open System
open System.IO
open System.Text.Json
open Klip


type XY = { x: float; y: float }

do
    let xy: XY[][][] =
        "D:/Git/_Euclid_/Klip/Test/Rhino/polysXY.json"
        |> File.ReadAllText
        |> JsonSerializer.Deserialize<_>

    let xy: XY[][] =
        xy
        |> Array.map Array.head


    printfn $"Original Paths: {xy.Length}"
    for i = 1 to 6 do
        let scale = 10. ** float i

        // Klip:
        let kr =
            xy
            |> Paths64.createFromxyMembers scale
            |> Klipper.unionSelf
            |> Paths64.scaleDown scale

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

        printfn $"Klipper: Scale: {scale}, Result Paths: {cr.Count}\n-"
