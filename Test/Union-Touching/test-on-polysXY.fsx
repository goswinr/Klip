#r "../../bin/Release/netstandard2.0/Klip.dll"

open System
open System.IO
open System.Text.Json
open Klip

type XY = { x: float; y: float }

let xyy: XY[][][] =
    "../Rhino/polysXY.json"
    |> File.ReadAllText
    |> JsonSerializer.Deserialize<_>

let xy: XY[][] =
    xyy
    |> Array.map Array.head


printfn $"Original Paths: {xy.Length}"

let kr =
    xy
    |> Paths64.createFromxyMembers
    |> Klipper.unionSelfChecked
printfn $"Klip Result Paths: {kr.Count}"
