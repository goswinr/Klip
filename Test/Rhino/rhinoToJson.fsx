#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "nuget: Rhino.Scripting"
#r "nuget: ResizeArrayT"
#r "nuget: Euclid.Rhino, 0.20.0"
#r "nuget: Str"


open System
open System.Text.Json
open ResizeArrayT
open Rhino.Scripting
open Euclid
open Str
type rs = RhinoScriptSyntax

// run on .\Test\benchSingle\union.3dm
//
let layer = "poly"

let objs = rs.ObjectsByLayer layer

let r = Random(1234)//to have the same offset always
let maxNoise = 1e-6

/// add random jitter to number, between 0.0 and f
let wobble x =
    x - maxNoise + (r.NextDouble() * 2.0 * maxNoise)

let polysOrig : Polyline2D ResizeArray =
    objs
    |> ResizeArray.sort //to have the same offset always
    |> ResizeArray.map rs.CoercePolyline
    |> ResizeArray.map Polyline2D.ofRhPolyline
    //|> ResizeArray.sortBy _.BoundingRectangle.MinX
    //|> ResizeArray.truncate 3 //keep json short for LLM to infer format

let polys : Polyline2D ResizeArray =
    polysOrig
    |> ResizeArray.map (fun pl ->
        for i=0 to pl.LastPointIndex do
            if i = pl.LastPointIndex then
                pl.Points[i] <- pl.FirstPoint
            else
                let p = pl.Points[i]
                pl.Points[i] <- Pt(wobble p.X, wobble p.Y)

        // offset outwards by double of max noise,  to ensure all polygon actually overlap
        Polyline2D.offset(pl,  maxNoise * -2.0,  uTurnBehavior = Offset2D.UTurn.Chamfer)
        )


let polyXY =
    polys
    |> ResizeArray.map (fun pl ->
        pl.Points
        |> ResizeArray.map (fun p -> {| x = p.X; y = p.Y |})
        |> ResizeArray.singleton
        )


let polyXYOrig =
    polysOrig
    |> ResizeArray.map (fun pl ->
        pl.Points
        |> ResizeArray.map (fun p -> {| x = p.X; y = p.Y |})
        |> ResizeArray.singleton
        )

let jsonXY     = JsonSerializer.Serialize(polyXY, JsonSerializerOptions(WriteIndented = true))
let jsonXYOrig = JsonSerializer.Serialize(polyXYOrig, JsonSerializerOptions(WriteIndented = true))
//printfn $"{Str.truncate 200 jsonXY}"
IO.File.WriteAllText("polysXY.json", jsonXY)
IO.File.WriteAllText("polysXYOrig.json", jsonXYOrig)
printfn $"wrote: {__SOURCE_DIRECTORY__}/polysXY.json"
printfn $"wrote: {__SOURCE_DIRECTORY__}/polysXYOrig.json"










