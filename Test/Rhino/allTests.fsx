#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"
#r "nuget: Str"
#r "nuget: ResizeArrayT"
#r "nuget: Fesher"
// #r "nuget: Euclid"

open Klip
// open Euclid
open Str
open ResizeArrayT
open System
open Fesher

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





for c in getTestCases() |> Seq.sortBy _.ClipType do
    Printf.gray $"{c.ClipType} CAPTION: {c.Caption} "
    let cl = new Clipper64<unit>()
    cl.AddSubject(Paths64.createDirectly  c.Subjects)
    cl.AddClip(Paths64.createDirectly  c.Clips)
    let r = cl.Execute(c.ClipType, c.FillRule) |> fst
    let area = Paths64.signedArea r
    let count = r.Count
    let relAreaDiff = if c.SolArea = 0.0 then 0.0 else abs (area - c.SolArea) / c.SolArea
    let countDiff = count - c.SolCount
    let relCountDiff = if c.SolCount = 0 then 0.0 else (float (abs (count - c.SolCount))) / (float c.SolCount)
    if relAreaDiff > 0.01  && abs (area - c.SolArea) > 1.0 then
        Printfn.red $"Area mismatch. Expected: {c.SolArea}, Got: %.2f{area}: Relative difference: %.2f{relAreaDiff * 100.0}%%"
    elif countDiff > 1 then
        Printfn.orange $"too Many. Expected: {c.SolCount}, Got: {count}, Relative difference: %.2f{relCountDiff * 100.0}%%"
    elif countDiff < -1 then
        Printfn.orchid $"too Few. Expected: {c.SolCount}, Got: {count}, Relative difference: %.2f{relCountDiff * 100.0}%%"
    else
        Printfn.green $"Ok"




