#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"

#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: ResizeArrayT, 0.26.0"
#r "nuget: Fesher, 0.5.0"
#r "nuget: Euclid.Rhino,0.30.1"

open System
open Rhino.Scripting
open Rhino.Scripting.FSharp
open Rhino.Geometry
open ResizeArrayT
open Fesher
open Euclid
open Klip
type rs = RhinoScriptSyntax
module R = ResizeArray

// let input2 : ResizeArray<Polyline2D> =
    // ResizeArray [ 
  // Polyline2D.create [| Pt(0.049991272592240794, 0.050008725884672124); Pt(0.06999127228762338, 0.05001221654315839); Pt(0.0699877816291371, 0.07001221623854098); Pt(0.03998778208606323, 0.07000698025081158); Pt(0.03998906893304115, 0.06263389025429993); Pt(0.04998906878073244, 0.06263563558354304); Pt(0.049991272592240794, 0.050008725884672124) |]
  // Polyline2D.create [| Pt(0.02999301822610134, 0.04000523537849456); Pt(0.049993017921483925, 0.04000872603698083); Pt(0.04998906878073244, 0.06263563558354304); Pt(0.029989069085349856, 0.06263214492505678); Pt(0.02999301822610134, 0.04000523537849456) |]

    // ]

let input : ResizeArray<Polyline2D> =
    ResizeArray [ 
        Polyline2D.create [|    
            Pt(-50, 50)
            Pt(-50, 70)
            Pt(-70, 70)
            Pt(-70, 40)
            Pt(-62, 40)
            Pt(-62, 50.0000000001)
            Pt(-50, 50)      
            |]               
        Polyline2D.create [| 
            Pt(-40, 30)      
            Pt(-40, 50)      
            Pt(-62, 50.00000000001)
            Pt(-62, 30)
            Pt(-40, 30) 
            |]
    ]

let draw lay (ps:Klip.Paths64<unit>) =
    for p in ps do
        // Printfn.gray $"{p.PointCount} points on {lay}"
        p.XYs
        |> Seq.chunkBySize 2
        |> Seq.map (fun xy -> Point3d(xy[0], xy[1], 0) )
        |> Polyline
        |> fun p -> p.Add(p.First); p
        // |> fun p -> p.RemoveNearlyEqualSubsequentPoints(1e-6); p
        |> fun p -> p.ToNurbsCurve() // nurbs allow duplicate points
        |> rs.Ot.AddCurve
        |> rs.setLayer $"draw::{lay}"

let print (ps : ResizeArray<Polyline2D> ) =
    ps
    |> R.map Polyline2D.asFSharpCode
    |> R.iter (Printfn.gray "  %s")
    
let printK (ps : Paths64<_> ) =
    ps
    |> R.map (_.XYs >> Polyline2D.createDirectly) 
    |> print   

let union(ps:Klip.Paths64<unit>) =
    let c = Clipper64()
    // c.ColinearityTolerance <- 1e-9
    // c.SnapXandY <- false
    c.AddPaths(Paths64.ensurePositiveOrientations ps, PathType.Subject)
    c.Execute(ClipType.Union, FillRule.NonZero) |> fst


let run1() =
    let ps =
        input
        |>  R.map Polyline2D.asPoints
        |>  Paths64.createFromXYMembers
    
    
    Printfn.gray "Input:"
    print input
    let res = union ps

    let r0 = res[0]
    if r0.PointCount = 8 then
        Printfn.green $"OK" 
    else
        Printfn.red $"FailL: not 8 but {r0.PointCount} points "
        
    Printfn.gray "Result:"
    printK res
        
    draw $"res" res 

run1()































