(* ******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  11 October 2025                                                 *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  Path Offset (Inflate/Shrink)                                    *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
****************************************************************************** *)

// ported to TypeScript at https://github.com/countertype/clipper2-ts
// then ported to F# and simplified here:

namespace Klip

open Klip.Null
open Klip.LipInternal


/// High-level convenience wrappers over Clipper64
module Polygon =

    /// Performs a boolean operation between `subject` and `clip` based on the specified `ClipType` and `FillRule`.
    let booleanOp (clipType:ClipType, subject:Paths64, clip:Paths64, [<OPT;DEF(FillRule.EvenOdd)>]fillRule:FillRule) : Paths64 =
        let solution = Paths64()
        if isNull' subject then
            solution
        else
            let c = Clipper64()
            c.AddPaths(subject, PathType.Subject)
            if isNotNull clip then
                c.AddPaths(clip, PathType.Clip)
            c.Execute(clipType, fillRule, solution) |> ignore
            solution

    /// Performs the intersection operation between `subject` and `clip` based on the specified `FillRule`.
    let intersect (subject:Paths64, clip:Paths64, [<OPT;DEF(FillRule.EvenOdd)>]fillRule:FillRule) : Paths64 =
        booleanOp (ClipType.Intersection, subject, clip, fillRule)

    /// Performs the union operation between `subject` and `clip` based on the specified `FillRule`.
    let union (subject:Paths64, clip:Paths64, [<OPT;DEF(FillRule.EvenOdd)>]fillRule:FillRule) : Paths64 =
        booleanOp (ClipType.Union, subject, clip, fillRule)

    /// Performs the union operation on a single subject path, resolving self-intersections.
    let unionSelf (subject:Paths64, [<OPT;DEF(FillRule.EvenOdd)>]fillRule:FillRule) : Paths64 =
        booleanOp (ClipType.Union, subject, null, fillRule)

    /// Performs the difference operation (subject regions that are not in the clip region) based on the specified `FillRule`.
    let difference (subject:Paths64, clip:Paths64, [<OPT;DEF(FillRule.EvenOdd)>]fillRule:FillRule) : Paths64 =
        booleanOp (ClipType.Difference, subject, clip, fillRule)

    /// Performs the XOR operation (regions in either subject or clip but not both) based on the specified `FillRule`.
    let xor (subject:Paths64, clip:Paths64, [<OPT;DEF(FillRule.EvenOdd)>]fillRule:FillRule) : Paths64 =
        booleanOp (ClipType.Xor, subject, clip, fillRule)

    /// Performs a boolean operation between `subject` and `clip` based on the specified `ClipType` and `FillRule`,
    /// returning the result as a `PolyTree64` which preserves the parent-child contour relationships.
    let booleanOpWithPolyTree ( clipType:ClipType,
                                subject:Paths64,
                                clip:Paths64,
                                polyTree:PolyTree64,
                                [<OPT;DEF(FillRule.EvenOdd)>]fillRule:FillRule) : unit =
        if isNotNull subject then
            let c = Clipper64()
            c.AddPaths(subject, PathType.Subject)
            if isNotNull clip then
                c.AddPaths(clip, PathType.Clip)
            c.Execute(clipType, fillRule, polyTree) |> ignore

    let rec private _addPolyNodeToPaths (polyPath: PolyPath64) (paths: Paths64) : unit =
        if isNotNull polyPath.Poly && polyPath.Poly.Count > 0 then
            paths.Add(polyPath.Poly)
        for i = 0 to polyPath.Count - 1 do
            _addPolyNodeToPaths (polyPath.Child(i)) paths

    /// Flattens a `PolyTree64` structure into a plain `Paths64` list of contours.
    let polyTreeToPaths64 (polyTree:PolyTree64) : Paths64 =
        let result = Paths64()
        for i = 0 to polyTree.Count - 1 do
            _addPolyNodeToPaths (polyTree.Child(i)) result
        result
