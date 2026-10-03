#r "C:/Program Files/Rhino 8/System/RhinoCommon.dll"
#r "D:/Git/_Euclid_/Klip/bin/Release/netstandard2.0/Klip.dll"

#r "nuget: Rhino.Scripting.FSharp, 0.14.0"
#r "nuget: Euclid.Rhino,0.51.0" // same Euclid version as Euclid.Kontur
#r "nuget: Euclid.Kontur, 0.1.0"
#r "nuget: Fesher, 0.5.0"
#r "nuget: Clipper2, 2.0.0"

open Fesher
open Euclid
open Klip
open Rhino.Scripting.FSharp
open Rhino.Scripting

type rs = RhinoScriptSyntax


// Reproduction of https://github.com/countertype/clipper2-ts/pull/35
// (same bug upstream: https://github.com/AngusJohnson/Clipper2/pull/1109)
//
// A NonZero union of a few thin triangles returns a region that is in none of the inputs.
// In exact arithmetic triangles 2 and 3 of `minimal` do not touch - there is a narrow crack
// (0.17 wide) between triangle 2's left edge and triangle 3's corner (-22, 51). Rounding the point
// where that edge crosses triangle 1, (0.94, 0.13), to (0, 0) swings the edge past the corner and
// closes the crack, so the pocket at (-7, 7) becomes a small hole. fixSelfIntersects -> doSplitOp then splits that hole off as a smaller loop of
// opposite orientation and drops it as a "spurious twist", so the remaining outer ring fills the hole.
//
// Klip sweeps on unrounded floats, so the crack should stay open and the probe points stay outside.
// Clipper2 (NuGet) runs on the same input: Paths64 (the integer API, as in the report), its
// PolyTree64 variant, and PathsD at a few precisions.
// Euclid.Kontur (https://github.com/goswinr/Euclid.Kontur, also float based, planar graph instead of
// a Vatti sweep) runs it as one NonZero Kontur via `simplify`, and as one Kontur per ring via `unionAll`.
//
// Every result is checked with an independent winding-number test at probe points that lie in
// none of the inputs, and with Klip and Kontur differences for the area outside / missing from the input.
// Both area measures are shown, because each engine is blind to slivers that it drops itself.
// Everything is drawn shifted so the first probe point sits at the origin.
//
// Observed (Klip 4.0.0, Euclid.Kontur 0.1.0, Clipper2 2.0.0 NuGet):
//  - fogWalk: Clipper2 Paths64 / PolyTree64 / PathsD prec 0 return only the outer ring, the pocket
//    (area ~5.2e8) is filled and the pocket probe winds 1. Clipper2 PathsD prec 2 keeps it.
//    Kontur returns one ring that runs through the crack into the pocket, exact area.
//    Klip with the default AngleTolerance keeps the pocket but fills the crack leading to it: rings 2 and 3
//    share the vertex (3307463, 2428565) and then diverge to 0.092 apart over 36537 units (sin angle 2.5e-6),
//    far below the default colinearity tolerance (1e-3), so that spike vertex is dropped, the pocket becomes
//    a separate hole and a 5275 area sliver outside the input is filled (the crack probe winds 1;
//    only the Kontur area measure sees it). With AngleTolerance <= 1e-4 degrees Klip matches Kontur.
//  - minimal: the C# Clipper2 does not reproduce it (the C++ and TS ports do): it keeps the hole as a
//    twisted part of the single ring, so the probe winds 0. Its rounded intersection points still leave
//    ~110 area outside the input. Klip and Kontur return one ring through the 0.17 wide crack, exact area 1169.


/// One flat x0,y0,x1,y1,... list per ring (same layout as Klip.Path64.XYs)
type Rings = ResizeArray<ResizeArray<float>>

/// Probes are points inside none of the inputs, the first one is also the drawing origin.
type Case = { Name: string; Input: Rings; Probes: (string * float * float) list }

let mkRings (xss: float list list) : Rings =
    xss |> List.map ResizeArray |> ResizeArray

// the three triangles from the PR description; (-7, 7) is inside none of them
let minimal = {
    Name = "minimal"
    Input = mkRings [
        [  91.0;  7.0;  -145.0; -11.0;  -141.0; -15.0 ]
        [ -28.0; 75.0;   -33.0;  76.0;     1.0;   0.0 ]
        [ -22.0; 51.0;   -39.0;  76.0;   -25.0;  -2.0 ]
        ]
    Probes = [ ("pocket", -7.0, 7.0) ] }

// tests/test-data/union-pocket.json from the PR: 3 rings, 15 vertices,
// reduced from a union of many visibility polygons ("fog walk")
let fogWalk = {
    Name = "fogWalk"
    Input = mkRings [
        [ 3511637.0; 2325895.0;  3511637.0; 2335126.0;  3478869.0; 2332678.0
          3478869.0; 2333168.0;  3152456.0; 2308519.0;  3157800.0; 2304109.0 ]
        [ 3363269.0; 2324438.0;  3314737.0; 2428271.0;  3307463.0; 2428565.0;  3353898.0; 2323730.0 ]
        [ 3339731.0; 2322660.0;  3322260.0; 2395158.0;  3307463.0; 2428565.0
          3298589.0; 2428922.0;  3318821.0; 2321081.0 ]
        ]
    Probes = [
        "pocket", 3274.55 * 1024.0, 2269.71 * 1024.0 // the PR's probe
        // halfway up the crack that leads from the pocket to the vertex (3307463, 2428565) shared by rings 2 and 3,
        // midway between ring 2's edge and ring 3's edge, which are 0.046 apart here
        "crack", 3314861.5210, 2411861.5093 ] }


/// Winding number of point (x, y) against all rings (same test as the PR's union-pocket.test.ts).
let winding (x: float) (y: float) (ps: Rings) : int =
    let mutable w = 0
    for xys in ps do
        let n = xys.Count / 2
        for i = 0 to n - 1 do
            let j = (i + 1) % n
            let ax, ay = xys[2*i], xys[2*i+1]
            let bx, by = xys[2*j], xys[2*j+1]
            let cross = (bx - ax) * (y - ay) - (x - ax) * (by - ay)
            if ay <= y then
                if by > y && cross > 0.0 then w <- w + 1
            elif by <= y && cross < 0.0 then
                w <- w - 1
    w

// ---------- conversions ----------

let toKlip (ps: Rings) : Paths64<unit> =
    ps |> Seq.map ResizeArray |> ResizeArray |> Paths64.createFrom

let ofKlip (ps: Paths64<unit>) : Rings =
    ps |> Seq.map (fun p -> ResizeArray p.XYs) |> ResizeArray

let toC64 (ps: Rings) : Clipper2Lib.Paths64 =
    let r = Clipper2Lib.Paths64()
    for xys in ps do
        let p = Clipper2Lib.Path64()
        for i in 0 .. 2 .. xys.Count - 1 do
            p.Add(Clipper2Lib.Point64(int64 xys[i], int64 xys[i+1]))
        r.Add p
    r

let ofC64 (ps: Clipper2Lib.Paths64) : Rings =
    ps
    |> Seq.map (fun p -> p |> Seq.collect (fun pt -> [ float pt.X; float pt.Y ]) |> ResizeArray)
    |> ResizeArray

let toCD (ps: Rings) : Clipper2Lib.PathsD =
    let r = Clipper2Lib.PathsD()
    for xys in ps do
        let p = Clipper2Lib.PathD()
        for i in 0 .. 2 .. xys.Count - 1 do
            p.Add(Clipper2Lib.PointD(xys[i], xys[i+1]))
        r.Add p
    r

let ofCD (ps: Clipper2Lib.PathsD) : Rings =
    ps
    |> Seq.map (fun p -> p |> Seq.collect (fun pt -> [ pt.x; pt.y ]) |> ResizeArray)
    |> ResizeArray

/// Kontur needs closed Polyline2Ds: the first point repeated as last point
let toPolylines (ps: Rings) : ResizeArray<Polyline2D> =
    ps |> Seq.map (fun xys -> ResizeArray xys |> Polyline2D.createDirectly |> Polyline2D.close 0.0) |> ResizeArray

// Euclid.FillRule, not Klip.FillRule: both namespaces are open and Klip's shadows Euclid's
let toKontur (ps: Rings) : Kontur =
    Kontur.create(toPolylines ps, Euclid.FillRule.NonZero)

/// drops the repeated closing point of each Kontur path
let ofKontur (k: Kontur) : Rings =
    k.Paths |> Seq.map (fun p -> p.XYs.GetRange(0, p.XYs.Count - 2)) |> ResizeArray

// ---------- the unions under test ----------

let newKlip (angleTolerance: float option) (ps: Rings) =
    let c =
        match angleTolerance with
        | Some deg -> Clipper64(tolerance = 1e-5, angleTolerance = deg) // 1e-5 is the default tolerance
        | None     -> Clipper64()
    c.AddPaths(toKlip ps, PathType.Subject)
    c

let unionKlip angleTolerance (ps: Rings) : Rings =
    (newKlip angleTolerance ps).Execute(ClipType.Union, FillRule.NonZero) |> fst |> ofKlip

let unionKlipTree (ps: Rings) : Rings =
    (newKlip None ps).ExecutePolyTree(ClipType.Union, FillRule.NonZero) |> fst |> Klipper.polyTreeToPaths64 |> ofKlip

let unionKonturSimplify (ps: Rings) : Rings =
    toKontur ps |> Kontur.simplify |> ofKontur

let unionKonturAll (ps: Rings) : Rings =
    toPolylines ps |> Seq.map (fun p -> Kontur.ofPolyline(p, Euclid.FillRule.NonZero)) |> Kontur.unionAll |> ofKontur

let unionC64 (ps: Rings) : Rings =
    Clipper2Lib.Clipper.Union(toC64 ps, Clipper2Lib.FillRule.NonZero) |> ofC64

let unionC64Tree (ps: Rings) : Rings =
    let tree = Clipper2Lib.PolyTree64()
    Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Union, toC64 ps, null, tree, Clipper2Lib.FillRule.NonZero)
    Clipper2Lib.Clipper.PolyTreeToPaths64 tree |> ofC64

let unionCD (precision: int) (ps: Rings) : Rings =
    Clipper2Lib.Clipper.Union(toCD ps, null, Clipper2Lib.FillRule.NonZero, precision) |> ofCD

// ---------- drawing and checking ----------

let origin (case: Case) =
    let _, x, y = case.Probes.Head
    x, y

let draw (case: Case) (layer: string) (ps: Rings) =
    if rs.ContextIsRhino() then
        let ox, oy = origin case
        for xys in ps do
            if xys.Count >= 6 then
                let shifted = ResizeArray<float>(xys.Count)
                for i in 0 .. 2 .. xys.Count - 1 do
                    shifted.Add(xys[i]   - ox)
                    shifted.Add(xys[i+1] - oy)
                shifted
                |> Polyline2D.createDirectly
                |> Polyline2D.close 1e-6
                |> Polyline2D.toRhPolylineCurve
                |> rs.Ot.AddCurve
                |> rs.setLayer $"pr35::{case.Name}::{layer}"

let drawProbes (case: Case) =
    if rs.ContextIsRhino() then
        let ox, oy = origin case
        for name, x, y in case.Probes do
            rs.Ot.AddTextDot(name, Rhino.Geometry.Point3d(x - ox, y - oy, 0.0)) |> rs.setLayer $"pr35::{case.Name}::probes"

let check (case: Case) (engine: string) (res: Rings) =
    // area of the output outside the input, and of the input missing from the output, by both float engines
    let outsideK = Klipper.difference (toKlip case.Input) (toKlip res)
    let missingK = Klipper.difference (toKlip res) (toKlip case.Input)
    let outsideC = Kontur.difference (toKontur res) (toKontur case.Input)
    let missingC = Kontur.difference (toKontur case.Input) (toKontur res)
    let ws = [ for _, x, y in case.Probes -> winding x y res ]
    let msg =
        sprintf "%-26s %d paths, area %.1f, winding at probes %A, outside / missing input: by Klip %.1f / %.1f, by Kontur %.1f / %.1f"
            engine res.Count (Paths64.signedArea (toKlip res)) ws
            (Paths64.signedArea outsideK) (Paths64.signedArea missingK) outsideC.SignedArea missingC.SignedArea
    if ws |> List.forall ((=) 0) then Printfn.green "  OK    %s" msg
    else                              Printfn.red   "  FAIL  %s" msg
    draw case engine res
    ofKlip outsideK   |> draw case $"{engine} outside input (Klip)"
    ofKontur outsideC |> draw case $"{engine} outside input (Kontur)"


for case in [ minimal; fogWalk ] do
    let probes =
        case.Probes
        |> List.map (fun (n, x, y) -> sprintf "%s (%.4f, %.4f) winds %d" n x y (winding x y case.Input))
        |> String.concat "; "
    printfn $"\n{case.Name}: {case.Input.Count} input rings, input at probes: {probes}"
    draw case "input" case.Input
    drawProbes case

    check case "Klip::Klip"                      (unionKlip None       case.Input)
    check case "Klip::PolyTree"             (unionKlipTree        case.Input)
    check case "Klip::AngleTolerance 1e-4°" (unionKlip (Some 1e-4) case.Input)
    check case "Kontur::simplify"           (unionKonturSimplify  case.Input)
    check case "Kontur::unionAll"           (unionKonturAll       case.Input)
    check case "Clipper2::Paths64"          (unionC64             case.Input)
    check case "Clipper2::PolyTree64"       (unionC64Tree         case.Input)
    for precision in [ 0; 2 ] do
        check case $"Clipper2::PathsD prec {precision}" (unionCD precision case.Input)
