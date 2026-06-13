#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"

#r "nuget: Euclid,0.30.1"
#r "nuget: Clipper2"
#r "nuget: Fesher,  0.5.0"

// https://github.com/AngusJohnson/Clipper2/issues/1091
//
open Fesher
open Clipper2Lib
open Euclid



let a = Polyline2D.create [
    Pt(0.000499,  5.0) // Point a0
    Pt(0.000499, -1.0) // Point a1
    Pt(-3.0,  0.0)
    ]

let b = Polyline2D.create [
    Pt(0.0005,  1.0) 
    Pt(0.0005, -5.0) 
    Pt( 3.0,  0.0)
    ]

let input : ResizeArray<Polyline2D> =
    ResizeArray[a;b]
    
let inputK :Klip.Paths64<unit> =  
    input
    |> Seq.map _.XYs
    |> Klip.Paths64.createFromSeq 

let inputC : PathsD =  
    input
    |> Seq.map _.AsPoints
    |> Seq.map (fun pts -> pts |> Seq.map ( fun pt -> PointD(pt.X, pt.Y) )) 
    |> Seq.map PathD
    |> PathsD 
   

let run() =
    let c = Klip.Clipper64()
    
    let unionK(ps:Klip.Paths64<unit>) =
        // c.SnapXandY <- true
        // c.ColinearityTolerance <- 1e-8
        // c.MergeVertexTolerance <- 0.002
        c.SnapXandYTolerance <- 1e-3
        // let ps = Klip.Paths64.ensurePositiveOrientations ps
        c.AddPaths(ps, Klip.PathType.Subject)
        c.Execute(Klip.ClipType.Union, Klip.FillRule.NonZero) 
        |> fst
        
        
    let areaOkC (a:PathsD)  (b:PathsD) = 
        let aa = a |> Seq.sumBy (Clipper.Area>>abs) 
        let bb = b |> Seq.sumBy (Clipper.Area>>abs) 
        abs(aa-bb) < (max aa bb) * 0.01 // within 1 %
        
    let areaOkK (a:Klip.Paths64<_>)  (b:Klip.Paths64<_>) = 
        let aa = a |> Seq.sumBy Klip.Path64.absArea
        let bb = b |> Seq.sumBy Klip.Path64.absArea
        abs(aa-bb) < (max aa bb) * 0.01 // within 1 %    
        
    
    let resK  = unionK inputK 
    
    
    if resK.Count = 1 && areaOkK resK inputK then 
        Printfn.green $"Klip OK"
    else 
        Printfn.red $"Klip:{resK.Count}"
    Printfn.gray $"Klip: ColinearityTolerance {c.ColinearityTolerance}, MergeVertexTolerance {c.MergeVertexTolerance}, SnapXandYTolerance {c.SnapXandY}:{c.SnapXandYTolerance}"
    
    
    for i = 0 to 6 do 
        let resC = Clipper.Union(inputC, null,  FillRule.EvenOdd, precision = i)
        if resC.Count = 1 && areaOkC resC inputC then 
            Printfn.green $"Clipper2 OK precision {i}"
        else 
            Printfn.red $"Clipper2: precision {i}, PathsD count: {resC.Count}"
            
run()
    
    



