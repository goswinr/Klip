#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"

#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: Euclid.Rhino,0.51.0" // same Euclid version as Euclid.Kontur
#r "nuget: Euclid.Kontur, 0.1.0"
#r "nuget: Clipper2, 2.0.0"
#r "nuget: Fesher, 0.5.0"

open Fesher
open Euclid
open Klip
open Rhino.Scripting.FSharp
open Rhino.Scripting

type rs = RhinoScriptSyntax

let crvs = rs.GetObjects("Polys")

let pls =  
    crvs
    |> Seq.map rs.CoercePolyline
    |> Seq.map Polyline2D.ofRhPolyline


let k = Kontur.create(pls,  Euclid.FillRule.NonZero) 
let r = Kontur.simplifyWith 0.2 k
// let r = Kontur.unionAll



for p in r.Paths do  
    Polyline2D.draw p
    |> rs.setLayer "Result Kontur"
