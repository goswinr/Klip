#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"
#r "nuget: Str"
#r "nuget: ResizeArrayT"
#r "nuget: Fesher"

#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: Clipper2, 2.0.0"

open Klip
// open Euclid
open Str
open ResizeArrayT
open System
open Fesher
open Rhino.Geometry
open Rhino.Scripting
open Rhino.Scripting.FSharp

type rs = RhinoScriptSyntax

type TestCase = {
    Caption: string
    ClipType: ClipType
    FillRule: FillRule
    SolArea: float
    SolCount: int
    Subjects: ResizeArray<ResizeArray<float>>
    Clips: ResizeArray<ResizeArray<float>>
}


let getTestCases() : ResizeArray<TestCase> =
    let testCases = ResizeArray<TestCase>()
    let lns = System.IO.File.ReadAllLines("../tests/test-data/Polygons.txt")
    let mutable caption = ""
    let mutable clipType = ClipType.Intersection
    let mutable fillRule = FillRule.EvenOdd
    let mutable solArea = 0.0
    let mutable solCount = 0
    let mutable subjects = ResizeArray<ResizeArray<float>>()
    let mutable clips = ResizeArray<ResizeArray<float>>()
    let mutable subjectsNext = false
    let mutable clipsNext = false

    for ln in lns do
        if ln.StartsWith "CAPTION:" then
            if caption <> "" then
                testCases.Add {
                    Caption = caption
                    ClipType = clipType
                    FillRule = fillRule
                    SolArea = solArea
                    SolCount = solCount
                    Subjects = subjects
                    Clips = clips
                    }
            caption <- ln |> Str.delete "CAPTION:" |> Str.trim
            solArea <- 0.0
            solCount <- 0
            subjects <- ResizeArray<ResizeArray<float>>()
            clips <- ResizeArray<ResizeArray<float>>()
            subjectsNext <- false
            clipsNext <- false

        elif ln.StartsWith "CLIPTYPE:" then
            if caption = "" then failwithf "CLIPTYPE line found before CAPTION: %s" ln
            clipType <- ln |> Str.delete "CLIPTYPE:" |> Str.trim |> function
                                                                        | "INTERSECTION" -> ClipType.Intersection
                                                                        | "UNION" -> ClipType.Union
                                                                        | "DIFFERENCE" -> ClipType.Difference
                                                                        | "XOR" -> ClipType.Xor
                                                                        | _ -> failwithf "Unknown ClipType in line: %s" ln
        elif ln.StartsWith "FILLRULE:" then
            fillRule <- ln |> Str.delete "FILLRULE:" |> Str.trim |> function
                                                                        | "EVENODD" -> FillRule.EvenOdd
                                                                        | "NONZERO" -> FillRule.NonZero
                                                                        | "POSITIVE" -> FillRule.Positive
                                                                        | "NEGATIVE" -> FillRule.Negative
                                                                        | _ -> failwithf "Unknown FillRule in line: %s" ln

        elif ln.StartsWith "SOL_AREA:" then
            solArea <- ln |> Str.delete "SOL_AREA:" |> Str.trim |> float
        elif ln.StartsWith "SOL_COUNT:" then
            solCount <- ln |> Str.delete "SOL_COUNT:" |> Str.trim |> int
        elif ln.StartsWith "SUBJECTS" then
            subjectsNext <- true
            clipsNext <- false
        elif ln.StartsWith "CLIPS" then
            clipsNext <- true
            subjectsNext <- false
        elif subjectsNext then
            subjects.Add (ln.Split([|',';' '|], StringSplitOptions.RemoveEmptyEntries) |> Array.map  float |> ResizeArray )
        elif clipsNext then
            clips.Add (ln.Split([|',';' '|], StringSplitOptions.RemoveEmptyEntries) |> Array.map  float |> ResizeArray)

    printfn $"Loaded {testCases.Count} test cases"
    testCases

let drawRh lay (ps: Point3d seq) =
        ps
        |> Polyline
        |> fun p -> p.Add(p.First); p
        |> fun p -> p.RemoveNearlyEqualSubsequentPoints(1e-6); p
        |> fun p -> p.ToNurbsCurve() // nurbs allow duplicate points
        |> rs.Ot.AddCurve
        |> rs.setLayer lay

let drawKl lay (ps:Klip.Paths64<unit>) =
    for j, p in Seq.indexed ps do
        try
            if p.PointCount > 2 then
                p.XYs
                |> Seq.chunkBySize 2
                |> Seq.map (fun xy -> Point3d(xy[0], xy[1], 0) )
                |> drawRh lay
            elif p.PointCount > 0 then
                rs.AddTextDot($"too short", Point3d(p.XYs[0], p.XYs[1], 0) )
                |> rs.setLayer lay
        with e ->
            eprintfn $"failed to draw: {lay}::{j} with {p.PointCount} points"
            printfn $"{e}"

let drawCl2 lay (ps:Clipper2Lib.Paths64) =
    for j, p in Seq.indexed ps do
        try
            if p.Count > 2 then
                p
                |> Seq.map (fun xy -> Point3d(float xy.X, float xy.Y, 0) )
                |> drawRh lay

                // rs.AddTextDot((if Clipper2Lib.Clipper.IsPositive p then "+" else "-") ,  Point3d(float p[1].X,float p[1].Y, 0.0 ))
                // |> rs.setLayer lay
            else
                rs.AddTextDot($"small", Point3d(float p.[0].X, float p.[1].Y, 0) )
                |> rs.setLayer lay
        with e ->
            eprintfn $"failed: {lay}::{j} with {p.Count} points"
            printfn $"{e}"


for c in getTestCases() |> Seq.sortBy _.ClipType do
    //Klip
    let cl = new Clipper64<unit>()
    let subject = Paths64.createDirectly c.Subjects
    let clip = Paths64.createDirectly  c.Clips
    cl.AddSubject subject
    cl.AddClip clip
    let r =  cl.Execute(c.ClipType, c.FillRule) |> fst

    //Clipper2
    let clipper2Subject =
        let p = Clipper2Lib.Paths64()
        for arr in c.Subjects do
            let path = Clipper2Lib.Path64()
            for i in 0 .. 2 .. arr.Count - 1 do
                path.Add(Clipper2Lib.Point64(int64 arr[i], int64 arr[i+1]))
            p.Add(path)
        p
    let clipper2Clip =
        let p = Clipper2Lib.Paths64()
        for arr in c.Clips do
            let path = Clipper2Lib.Path64()
            for i in 0 .. 2 .. arr.Count - 1 do
                path.Add(Clipper2Lib.Point64(int64 arr[i], int64 arr[i+1]))
            p.Add(path)
        p
    let clipper2ClipType = enum<Clipper2Lib.ClipType>(int c.ClipType)
    let clipper2FillRule = enum<Clipper2Lib.FillRule>(int c.FillRule)
    let clipper2Solution = Clipper2Lib.Clipper.BooleanOp(clipper2ClipType, clipper2Subject, clipper2Clip,  clipper2FillRule)



    // print :

    let area = Paths64.signedArea r
    let count = r.Count
    let relAreaDiff = if c.SolArea = 0.0 then 0.0 else abs (area - c.SolArea) / c.SolArea
    let countDiff = count - c.SolCount
    let relCountDiff = if c.SolCount = 0 then 0.0 else (float (abs (count - c.SolCount))) / (float c.SolCount)

    Printf.gray $"{c.ClipType} CAPTION: {c.Caption} "
    if relAreaDiff > 0.01  && abs (area - c.SolArea) > 1.0 then
        Printfn.red $"Area mismatch. Expected: {c.SolArea}, Got: %.2f{area}: Relative difference: %.2f{relAreaDiff * 100.0}%%"
    elif countDiff > 1 then
        Printfn.orange $"too Many. Expected: {c.SolCount}, Got: {count}, Relative difference: %.2f{relCountDiff * 100.0}%%"
    elif countDiff < -1 then
        Printfn.orchid $"too Few. Expected: {c.SolCount}, Got: {count}, Relative difference: %.2f{relCountDiff * 100.0}%%"
    else
        Printfn.green $"Ok"



        // if c.Caption = "193." then
        if c.Caption = "18." then
        // if c.Caption = "19." then
            drawKl $"{c.Caption}::subject" subject
            drawKl $"{c.Caption}::clip" clip
            drawKl $"{c.Caption}::solution" r
            drawCl2 $"{c.Caption}::solution Clipper2" clipper2Solution
            rs.AddTextDot($"relCountDiff %.2f{relCountDiff * 100.0}%% , relAreaDiff: %.2f{relAreaDiff * 100.0}%%", Point3d(0,0,0)) |> rs.setLayer $"{c.Caption}::info"



