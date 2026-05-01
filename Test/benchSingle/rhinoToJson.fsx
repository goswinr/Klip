#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "nuget: Rhino.Scripting"
#r "nuget: ResizeArrayT"
#r "nuget: Euclid.Rhino"
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

let r = Random()
let maxNoise = 1e-6

/// add randum jitter to number, between 0.0 and f
let wobble x =  
    x - maxNoise + (r.NextDouble() * 2.0 * maxNoise) 

let polys : Polyline2D ResizeArray =
    objs
    |> ResizeArray.map rs.CoercePolyline
    |> ResizeArray.map Polyline2D.ofRhPolyline
    |> ResizeArray.sortBy _.BoundingRectangle.MinX
    //|> ResizeArray.truncate 3 //keep json short for LLM to infer format
    |> ResizeArray.map (fun pl ->
        for i=0 to pl.LastPointIndex do
            let p = pl.Points[i]
            //pl.Points[i] <- Pt(p.X, p.Y)
            pl.Points[i] <- Pt(wobble p.X, wobble p.Y)
        pl
        |> Polyline2D.close
        // offset outwards by double of max noise,  to ensure all polygon actually overlap
        |> fun p -> Polyline2D.offset(p,  maxNoise * -2.0,  uTurnBehavior = Offset2D.UTurn.Chamfer)
        )


let polyXY =
    polys
    |> ResizeArray.map (fun pl ->
        pl.Points
        |> ResizeArray.map (fun p -> {| x = p.X; y = p.Y |})
        |> ResizeArray.singleton
        )

let jsonXY = JsonSerializer.Serialize(polyXY, JsonSerializerOptions(WriteIndented = true))
//printfn $"{Str.truncate 200 jsonXY}"
IO.File.WriteAllText("polysXY.json", jsonXY)
printfn $"wrote: {__SOURCE_DIRECTORY__}/polysXY.json"










