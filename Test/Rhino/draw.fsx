#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"
#r "nuget: Clipper2, 2.0.0"
#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: ResizeArrayT, 0.26.0"
#r "nuget: Fesher, 0.5.0"

open System
open System.IO
open System.Text.Json
open Klip
open Clipper2Lib
open Rhino.Scripting
open Rhino.Scripting.FSharp
open Rhino.Geometry
open ResizeArrayT
open Fesher

type rs = RhinoScriptSyntax

type XY = { x: float; y: float }

module U =   
    
    let readPolyJson path : XY[][] =
        path
        |>  File.ReadAllText
        |>  JsonSerializer.Deserialize<XY[][][]>
        |>  Array.map Array.head
           
    let selectInRhino() :XY[][]  =
        //rs.GetObjectsAndRemember("Select polygons",  filter = rs.Filter.Curve)
        rs.GetObjects("Select polygons",  filter = rs.Filter.Curve)
        |> Seq.map rs.CoercePolyline
        |> Seq.map (  Seq.map (fun p -> {x=p.X; y=p.Y}) >> Array.ofSeq )
        |> Array.ofSeq     

    let drawRh lay (ps: Point3d seq) =  
        ps
        |> Polyline
        |> fun p -> p.Add(p.First); p
        |> fun p -> p.RemoveNearlyEqualSubsequentPoints(1e-6); p
        |> fun p -> p.ToNurbsCurve() // nurbs allow duplicate points
        |> rs.Ot.AddCurve
        |> rs.setLayer lay 
        
    let printXY(xy:XY[][]) = 
        let f (x:float) =  
            let s = $"{x}"
            if s.Contains "." then s else $"{s}.0"
        
        for i, ps in Seq.indexed xy do 
            printfn $"let p{i} = PathD ["
            let l = ps.[ps.Length-1]
            for j, p in Seq.indexed ps do  
                if j<>0 || p.x <> l.x || p.y <> l.y then // skip start if same as end
                    printfn $"    PointD({f p.x}, {f p.y})" 
            printfn "    ]"
    
    let printKlip (scale)  (xy:XY[][]) = 
        
        let f (x:float) =  
            let s = $"{x}"
            if s.Contains "." then s else $"{s}.0"
        
        for i, ps in Seq.indexed xy do 
            printfn $"let p{i} = Path64.createFromSeq {scale} ["
            let l = ps.[ps.Length-1]
            for j, p in Seq.indexed ps do  
                if j<>0 || p.x <> l.x || p.y <> l.y then // skip start if same as end
                    printfn $"    {f p.x}; {f p.y}" 
            printfn "    ]"        
            
            
            
    let printXYInt scale (xy:XY[][]) = 
        let tint(x) = int(round ((10. ** float scale)*x) )
        for i, ps in Seq.indexed xy do 
            printfn $"let p{i} = Path64 ["
            let l = ps.[ps.Length-1]
            for j, p in Seq.indexed ps do  
                if j<>0 || p.x <> l.x || p.y <> l.y then // skip start if same as end
                    printfn $"    Point64({tint p.x}L, {tint p.y}L)" 
            printfn "    ]"   
    
    
    let drawXY (ps:XY[][]) =
        for j, p in Seq.indexed ps do
            try
                if p.Length > 2 then
                    p
                    |> Seq.map (fun xy -> Point3d(xy.x, xy.y, 0) )
                    |> drawRh "polysXY"
                else
                    rs.AddTextDot($"short", Point3d(p[0].x, p[1].y, 0) )
                    |> rs.setLayer "polysXY"
            with e ->
                eprintfn $"failed: polysXY with {p.Length} points"
                printfn $"{e}"
    
    
    let drawK tool sc (ps:Paths64<_>) =
        for j, p in Seq.indexed ps do
            try
                if p.PointCount > 2 then
                    p.XYs
                    |> Seq.chunkBySize 2
                    |> Seq.map (fun xy -> Point3d(xy[0], xy[1], 0) )
                    |> drawRh $"scale {sc}::{tool}"
                else
                    rs.AddTextDot($"short", Point3d(p.XYs[0], p.XYs[1], 0) )
                    |> rs.setLayer $"scale {sc}::{tool}"
            with e ->
                eprintfn $"failed: {tool}::scale {sc}::{j} with {p.PointCount} points"
                printfn $"{e}"


    let drawD tool sc (ps:PathsD) =
        for j, p in Seq.indexed ps do
            try
                if p.Count > 2 then
                    p
                    |> Seq.map (fun xy -> Point3d(xy.x, xy.y, 0) )
                    |> drawRh $"scale {sc}::{tool}"
                else
                    rs.AddTextDot($"small", Point3d(p.[0].x, p.[1].y, 0) )
                    |> rs.setLayer $"scale {sc}::{tool}"
            with e ->
                eprintfn $"failed: {tool} scale {sc}::{j} with {p.Count} points"
                printfn $"{e}"
                
    let draw64 lay (ps:Paths64) =
        for j, p in Seq.indexed ps do
            try
                if p.Count > 2 then
                    p
                    |> Seq.map (fun xy -> Point3d(float xy.X, float xy.Y, 0) )
                    |> drawRh $"Clipper2-64::{lay}"
                    
                    rs.AddTextDot((if Clipper.IsPositive p then "+" else "-") ,  Point3d(float p[1].X,float p[1].Y, 0.0 )) 
                    |> rs.setLayer $"Clipper2-64::{lay}"
                else
                    rs.AddTextDot($"small", Point3d(float p.[0].X, float p.[1].Y, 0) )
                    |> rs.setLayer $"Clipper2-64::{lay}"
            with e ->
                eprintfn $"failed: Clipper2-64::{lay}::{j} with {p.Count} points"
                printfn $"{e}"            
    
                
    let convertD(xy:XY[][]) : PathsD = 
        let ps = PathsD()
        for xs in xy do
            let c = PathD()
            for p in xs do
                c.Add(PointD(p.x, p.y))
                
            // reverse    
            if Clipper.IsPositive c |> not then  
                ps.Add(Clipper.ReversePath c) 
            else 
                ps.Add(c)
        ps
        
        
    let unionFirstWithRest scale (p:PathsD) =  
        let p1 = PathsD [ResizeArray.head p]
        let p2 = PathsD (ResizeArray.removeAt 0 p) 
        //printfn $"p1 count: {p1.Count}"
        //printfn $"p2 count: {p2.Count}"
        Clipper.BooleanOp( ClipType.Union, p1, p2, FillRule.NonZero, precision = scale)
 
let polys() = 
        
    let xy =  
        // U.readPolyJson "D:/Git/_Euclid_/Klip/Test/Rhino/polysXY.json"
        U.selectInRhino() 
   
    printfn $"Original Paths: {xy.Length}"
    rs.DisableRedraw()
    U.drawXY xy
    U.printKlip (10. ** float 2) xy 
    
    
    
    //let xys = xy |> Array.sortBy ( fun ps -> ps |> Array.map ( fun p -> p.x + p.y) |> Array.min) 
    
    for i = 2 to 2 do
        let scale = 10. ** float i
        let psD = xy |> U.convertD
        let psK = xy |> Paths64.createFromxyMembers scale
        
        // Klip:
        let kr =
            psK
            |> Klipper.unionSelf
            |> Paths64.scaleDown scale
        U.drawK "Klip" i kr
        Printfn.darkGreen $"Klip:    Scale: {scale}, Result Paths: {kr.Count}" 
        
        // KlipR:
        let rec loopKlip k (p:Paths64<unit>) = 
            //let r = U.unionFirstWithRest i r
            let r = Klipper.booleanOp( Klip.ClipType.Union, p, null, Klip.FillRule.NonZero,  None)
            if k < 4 && r.Count > 1 then  
                Printfn.lightRed $"Loop {k} : Result Paths: {r.Count} ..."
                loopKlip (k+1) r
            else
                if r.Count <> 1 then 
                    //U.printXY xy
                    //U.printXYInt i xy
                    Printfn.red $"Loops {k} : Scale: {scale}, Result Paths: {r.Count}"
                else 
                    Printfn.green $"Loops {k} : Scale: {scale}, Result Paths: {r.Count}" 
                    // printfn $"Clipper2.IsPositive:{Clipper.IsPositive r[0]}"
                U.drawK "KlipR" i r 
        
        loopKlip 1 psK
        

        // Clipper2:
        let rec loopClipper2 k (p:PathsD) = 
            //let r = U.unionFirstWithRest i r
            let r = Clipper.BooleanOp( ClipType.Union, p, null, FillRule.NonZero, precision = i)
            if k < 4 && r.Count > 1 then  
                Printfn.blue $"Loop {k} : Result Paths: {r.Count} ..."
                loopClipper2 (k+1) r
            else
                if r.Count <> 1 then 
                    //U.printXY xy
                    //U.printXYInt i xy
                    Printfn.red $"Loops {k} : Scale: {scale}, Result Paths: {r.Count}"
                else 
                    Printfn.green $"Loops {k} : Scale: {scale}, Result Paths: {r.Count}" 
                    // printfn $"Clipper2.IsPositive:{Clipper.IsPositive r[0]}"
                U.drawD "Clipper2" i r 
        
        loopClipper2 1 psD
        
let one() = 
    let i = 5 //or 3 
    let xy = U.selectInRhino() 
    let mutable r = U.convertD xy
    r <- Clipper.BooleanOp( ClipType.Union, r, null, FillRule.NonZero, precision = i)
    r <- Clipper.BooleanOp( ClipType.Union, r, null, FillRule.NonZero, precision = i)
    U.drawD "one" r

let selfFail() =  
    // Using self Union fails:
    
    let p0 = Path64 [
        Point64(70L, 0L)
        Point64(20L, 0L)
        Point64(20L, 80L)
        Point64(70L, 80L)
        ]
    let p1 = Path64 [
        Point64(10L, 30L)
        Point64(24L, 30L)
        Point64(24L, 90L)
        Point64(10L, 90L)
        ]
    let pp0 = Paths64[p0]
    let pp1 = Paths64[p1]
    
    let r1 = Clipper.Union(pp0, pp1, FillRule.NonZero)
    printfn $"One Subject, one Clip Path64: count = {r1.Count}"
    
    let r2 = Clipper.Union(Paths64[p0; p1], FillRule.NonZero)
    printfn $"Two Subject Path64: count = {r2.Count}"
    
    U.draw64 "input" pp0
    U.draw64 "input" pp1
    U.draw64 "r2" r2
    U.draw64 "r1" r1
 
let selfFail2() = 
    for i=0 to 3 do 
        printfn $"i={i}:"
    
        let mutable a =
            [
            Point64(70L, 0L)
            Point64(20L, 0L)
            Point64(20L, 80L)
            Point64(70L, 80L)
            ]
    
        let mutable b =
            [
            Point64(10L, 30L)
            Point64(24L, 30L)
            Point64(24L, 90L)
            Point64(10L, 90L)
            ]
    
        if i = 1 then
            a <- List.rev a
        elif i = 2 then
            b <- List.rev b
        elif i = 3 then
            a <- List.rev a
            b <- List.rev b
    
        let ab = Paths64[ Path64 a; Path64 b ]
    
    
        let ap = if Clipper.IsPositive ab[0] then "positive" else "negative"
        let bp = if Clipper.IsPositive ab[1] then "positive" else "negative"
    
    
        let r = Clipper.Union(ab, FillRule.NonZero)
        U.draw64 $"FillRule.NonZero = {r.Count}, a = {ap}, b = {bp}" r
    
        let r = Clipper.Union(ab, FillRule.NonZero)
        U.draw64 $"FillRule.NonZero = {r.Count}, a = {ap}, b = {bp}" r
    
        let r = Clipper.Union(ab, FillRule.Positive)
        U.draw64 $"FillRule.Positive = {r.Count}, a = {ap}, b = {bp}" r
    
        let r = Clipper.Union(ab, FillRule.Negative)
        U.draw64 $"FillRule.Negative = {r.Count}, a = {ap}, b = {bp}" r


let removeSelfIntersection() = 
        
    let xy =  U.selectInRhino() 

    rs.DisableRedraw() 

    let psK = xy |> Paths64.createFromxyMembers 1.0

    let r = Klipper.booleanOp( Klip.ClipType.Union, psK, null, Klip.FillRule.Positive,  None)

    U.drawK "KlipR" 0 r 
    

      
    
    
    
// polys()
//one()
//selfFail()
//selfFail2()
removeSelfIntersection()

    
    
    
            
            
            
            
        
        
        
        
         
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
        
