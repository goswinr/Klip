(*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  14 December 2025                                                *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  This module contains simple functions that will likely cover    *
*              most polygon boolean needs, while also avoiding                 *
*              the inherent complexities of the other modules.                 *
* Thanks    :  Special thanks to Thong Nguyen, Guus Kuiper, Phil Stopford,     *
*           :  and Daniel Gosnell for their invaluable assistance with C#.     *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*******************************************************************************)

// ported to TypeScript at https://github.com/countertype/clipper2-ts
// then ported to F# and simplified here:

namespace Klip

open System
open Klip.Null
open Klip.KlipInternalTypes


// #region Klipper

/// High-level convenience wrappers over Clipper64.
/// Clipping operations will always return Positive oriented solutions
/// (unless the Clipper object's ReverseSolution property has been enabled).
/// This means that outer polygon contours will wind anti-clockwise (in Cartesian coordinates),
/// and inner hole contours will wind clockwise. And because paths in clipping solutions never intersect,
/// both NonZero and EvenOdd filling would correctly apply to the solution,
/// though it's usual to apply the same FillRule that was applied to the subject and clip paths during clipping.
///
/// A lot of effort has gone into returning solutions close to their simplest forms,
/// but there's no way to do this perfectly without significantly degrading performance.
/// So there will, on occasions, be solutions with polygons that are touching.
/// If this is problematic, then a follow up union operation will frequently bring these solutions to their simplest forms.
///
/// The library supports open path clipping, and this may also be performed concurrently with closed path clipping.
/// However, only subject paths may be open. Except in union operations,
/// the presence of closed subject paths will have no effect on open path solutions.
/// In union operations, open paths will be clipped wherever they overlap any closed paths
/// (regardless of whether they are subject or clip paths).
///
/// These wrappers use Clipper64's default tolerances, which are absolute values that must be
/// scaled to the input's coordinate magnitude. For custom tolerances, PreserveColinear,
/// ReverseSolution or open-path clipping, use the Clipper64 class directly.
[<RequireQualifiedAccess>]
module Klipper =



    /// Performs a boolean operation between `subject` and `clip` based on the specified `ClipType` and `FillRule`.
    /// The result outer path contours will have a positive orientation
    /// Considers all paths closed.
    let booleanOp (clipType:ClipType, subject:Paths64<unit>, clip:Paths64<unit>, fillRule:FillRule) : Paths64<unit> =
        if isNull' subject then
            invalidOp "Klip.Klipper.booleanOp: subject paths cannot be null. Only the clip paths can be null."
        let c = Clipper64()
        c.AddPaths(subject, PathType.Subject)
        if isNotNull clip then
            c.AddPaths(clip, PathType.Clip)
        c.Execute(clipType, fillRule)
        |> fst // get closed paths from tuple result, ignoring open paths


    /// Performs the intersection operation between `subject` and `clip` using the NonZero fill rule.
    let intersect (clip:Paths64<unit>) (subject:Paths64<unit>) : Paths64<unit> =
        booleanOp (ClipType.Intersection, subject, clip, FillRule.NonZero)


    /// Performs the union operation between `subject` and `clip` using the NonZero fill rule.
    let union (clip:Paths64<unit>) (subject:Paths64<unit>) : Paths64<unit> =
        booleanOp (ClipType.Union, subject, clip, FillRule.NonZero)


    /// Performs the union operation on the subject paths alone (no clip), using the NonZero fill rule.
    /// Path orientations are used as given: negatively oriented (clockwise) paths are treated as holes.
    /// If you need all paths forced to a positive orientation first, use `unionSelfChecked`.
    /// A positive orientation is a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let unionSelf (subject:Paths64<unit>) : Paths64<unit> =
        booleanOp (ClipType.Union, subject, null, FillRule.NonZero)


    /// Performs the union operation on simple paths without holes.
    /// Ensures that all paths have a positive orientation before performing the union.
    /// The input list is not modified; negatively oriented paths are replaced by reversed copies in a new list.
    /// A positive orientation is a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let unionSelfChecked (subject:Paths64<unit>) : Paths64<unit> =
        let positives = Paths64<unit>(subject.Count)
        for i = 0 to subject.Count - 1 do
            let s = Rarr.getIdx i subject
            if s.SignedArea < 0.0 then // ensure all paths have a positive orientation before performing the union. Negative paths would be considered holes.
                positives.Add(Geo.reversePath s)
            else
                positives.Add s
        booleanOp (ClipType.Union, positives, null, FillRule.NonZero)


    /// Performs the difference operation (subject regions that are not in the clip region) using the NonZero fill rule.
    let difference (clip:Paths64<unit>) (subject:Paths64<unit>) : Paths64<unit> =
        booleanOp (ClipType.Difference, subject, clip, FillRule.NonZero)

    /// Performs the XOR operation (regions in either subject or clip but not both) using the NonZero fill rule.
    let xor (clip:Paths64<unit>) (subject:Paths64<unit>) : Paths64<unit> =
        booleanOp (ClipType.Xor, subject, clip, FillRule.NonZero)


    /// Removes self-intersections from a positively oriented path by performing a union operation on it with itself.
    /// A positive orientation is a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let removeSelfIntersectionsPositive (subject:Path64<unit>) : Paths64<unit> =
        let r = ResizeArray<Path64<unit>>()
        r.Add subject
        booleanOp (ClipType.Union, r, null, FillRule.Positive)

    /// Removes self-intersections from a negatively oriented path by performing a union operation on it with itself.
    /// A negative orientation is a clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let removeSelfIntersectionsNegative (subject:Path64<unit>) : Paths64<unit> =
        let r = ResizeArray<Path64<unit>>()
        r.Add subject
        booleanOp (ClipType.Union, r, null, FillRule.Negative)


    /// Performs a boolean operation between `subject` and `clip` based on the specified `ClipType` and `FillRule`,
    /// returning the result as a `PolyTree64` which preserves the parent-child contour relationships.
    /// Considers all paths closed.
    let booleanOpPolyTree( clipType:ClipType, subject:Paths64<unit>, clip:Paths64<unit>, fillRule:FillRule) : PolyTree64<unit> =
        if isNull' subject then
            invalidOp "Klip.Klipper.booleanOpPolyTree: subject paths cannot be null. Only the clip paths can be null."
        let c = Clipper64()
        c.AddPaths(subject, PathType.Subject)
        if isNotNull clip then
            c.AddPaths(clip, PathType.Clip)
        c.ExecutePolyTree(clipType, fillRule)
        |> fst // get closed paths from tuple result, ignoring open paths


    let rec private recursivelyAddPolyNodeToPaths (polyPath: PolyPath64<unit>) (paths: Paths64<unit>) : unit =
        if isNotNull polyPath.Poly && polyPath.Poly.PointCount > 0 then
            paths.Add(polyPath.Poly)
        for i = 0 to polyPath.Count - 1 do
            recursivelyAddPolyNodeToPaths (polyPath.Child(i)) paths

    /// Flattens a `PolyTree64` structure into a plain `Paths64<unit>` list of contours.
    let polyTreeToPaths64 (polyTree:PolyTree64<unit>) : Paths64<unit> =
        let result = Paths64<unit>()
        for i = 0 to polyTree.Count - 1 do
            recursivelyAddPolyNodeToPaths (polyTree.Child(i)) result
        result


    /// Sets the process-wide default for Clipper64.ScanlineArrayThreshold, used by every
    /// Clipper64 constructed afterwards — including the ones these wrapper functions create
    /// internally. It is the size at which the engine switches its pending-scanline container
    /// from a small unsorted array (linear scan) to a max-heap plus hash-set (O(log n) operations).
    /// Performance tuning only — it never changes clipping results.
    /// Pass 0 to always use the heap+set, or a very large value to always use the array.
    /// The default is 64.
    let setDefaultScanlineArrayThreshold (count: int) : unit =
        if count < 0 then
            invalidArg "count" $"Klipper.setDefaultScanlineArrayThreshold: count must be 0 or more. Got {count}."
        Eng.defaultScanlineArrayThreshold <- count

    /// Gets the process-wide default for Clipper64.ScanlineArrayThreshold.
    /// See setDefaultScanlineArrayThreshold for what it does.
    let getDefaultScanlineArrayThreshold () : int =
        Eng.defaultScanlineArrayThreshold



// #region KlipperZ

/// High-level convenience wrappers over Clipper64 that support arbitrary Z values attached to vertices via a callback function.
/// Clipping operations will always return Positive oriented solutions
/// (unless the Clipper object's ReverseSolution property has been enabled).
/// This means that outer polygon contours will wind anti-clockwise (in Cartesian coordinates),
/// and inner hole contours will wind clockwise. And because paths in clipping solutions never intersect,
/// both NonZero and EvenOdd filling would correctly apply to the solution,
/// though it's usual to apply the same FillRule that was applied to the subject and clip paths during clipping.
///
/// A lot of effort has gone into returning solutions close to their simplest forms,
/// but there's no way to do this perfectly without significantly degrading performance.
/// So there will, on occasions, be solutions with polygons that are touching.
/// If this is problematic, then a follow up union operation will frequently bring these solutions to their simplest forms.
///
/// The library supports open path clipping, and this may also be performed concurrently with closed path clipping.
/// However, only subject paths may be open. Except in union operations,
/// the presence of closed subject paths will have no effect on open path solutions.
/// In union operations, open paths will be clipped wherever they overlap any closed paths
/// (regardless of whether they are subject or clip paths).
///
/// These wrappers use Clipper64's default tolerances, which are absolute values that must be
/// scaled to the input's coordinate magnitude. For custom tolerances, PreserveColinear,
/// ReverseSolution or open-path clipping, use the Clipper64 class directly.
[<RequireQualifiedAccess>]
module KlipperZ =


    /// Performs a boolean operation between `subject` and `clip` based on the specified `ClipType` and `FillRule`.
    /// The result outer path contours will have a positive orientation
    /// Considers all paths closed.
    let booleanOp (clipType:ClipType, subject:Paths64<'Z>, clip:Paths64<'Z>, fillRule:FillRule, zCallback:option<ZCallback64<'Z>>) : Paths64<'Z> =
        if isNull' subject then
            invalidOp "Klip.KlipperZ.booleanOp: subject paths cannot be null. Only the clip paths can be null."
        let c = Clipper64()
        c.ZCallback <- zCallback
        c.AddPaths(subject, PathType.Subject)
        if isNotNull clip then
            c.AddPaths(clip, PathType.Clip)
        c.Execute(clipType, fillRule)
        |> fst // get closed paths from tuple result, ignoring open paths

    /// Performs the intersection operation between `subject` and `clip` using the NonZero fill rule and a custom Z callback.
    let intersect (zCallback:option<ZCallback64<'Z>>) (clip:Paths64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Intersection, subject, clip, FillRule.NonZero, zCallback)

    /// Performs the union operation between `subject` and `clip` using the NonZero fill rule and a custom Z callback.
    let union (zCallback:option<ZCallback64<'Z>>) (clip:Paths64<'Z>) (subject:Paths64<'Z>)  : Paths64<'Z> =
        booleanOp (ClipType.Union, subject, clip, FillRule.NonZero, zCallback)

    /// Performs the union operation on the subject paths alone (no clip), using the NonZero fill rule and a custom Z callback.
    /// Path orientations are used as given: negatively oriented (clockwise) paths are treated as holes.
    /// If you need all paths forced to a positive orientation first, use `unionSelfChecked`.
    /// A positive orientation is a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let unionSelf (zCallback:option<ZCallback64<'Z>>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Union, subject, null, FillRule.NonZero, zCallback)

    /// Performs the union operation on simple paths without holes, with a custom Z callback.
    /// Ensures that all paths have a positive orientation before performing the union.
    /// The input list is not modified; negatively oriented paths are replaced by reversed copies in a new list.
    /// A positive orientation is a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let unionSelfChecked (zCallback:option<ZCallback64<'Z>>) (subject:Paths64<'Z>) : Paths64<'Z> =
        let positives = Paths64<'Z>(subject.Count)
        for i = 0 to subject.Count - 1 do
            let s = Rarr.getIdx i subject
            if s.SignedArea < 0.0 then // ensure all paths have a positive orientation before performing the union. Negative paths would be considered holes.
                positives.Add(Geo.reversePath s)
            else
                positives.Add s
        booleanOp (ClipType.Union, positives, null, FillRule.NonZero, zCallback)

    /// Performs the difference operation (subject regions that are not in the clip region) using the NonZero fill rule and a custom Z callback.
    let difference (zCallback:option<ZCallback64<'Z>>) (clip:Paths64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Difference, subject, clip, FillRule.NonZero, zCallback)

    /// Performs the XOR operation (regions in either subject or clip but not both) using the NonZero fill rule and a custom Z callback.
    let xor (zCallback:option<ZCallback64<'Z>>) (clip:Paths64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Xor, subject, clip, FillRule.NonZero, zCallback)

    /// Removes self-intersections from a positively oriented path by performing a union operation on it with itself.
    /// A positive orientation is a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let removeSelfIntersectionsPositive  (zCallback:option<ZCallback64<'Z>>) (subject:Path64<'Z>) : Paths64<'Z> =
        let r = ResizeArray<Path64<'Z>>()
        r.Add subject
        booleanOp (ClipType.Union, r, null, FillRule.Positive, zCallback)

    /// Removes self-intersections from a negatively oriented path by performing a union operation on it with itself.
    /// A negative orientation is a clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let removeSelfIntersectionsNegative (zCallback:option<ZCallback64<'Z>>) (subject:Path64<'Z>) : Paths64<'Z> =
        let r = ResizeArray<Path64<'Z>>()
        r.Add subject
        booleanOp (ClipType.Union, r, null, FillRule.Negative, zCallback)



    /// Performs a boolean operation between `subject` and `clip` based on the specified `ClipType` and `FillRule`,
    /// returning the result as a `PolyTree64` which preserves the parent-child contour relationships.
    /// Considers all paths closed.
    let booleanOpPolyTree( clipType:ClipType, subject:Paths64<'Z>, clip:Paths64<'Z>, fillRule:FillRule, zCallback:option<ZCallback64<'Z>> ) : PolyTree64<'Z> =
        if isNull' subject then
            invalidOp "Klip.KlipperZ.booleanOpPolyTree: subject paths cannot be null. Only the clip paths can be null."
        let c = Clipper64()
        c.ZCallback <- zCallback
        c.AddPaths(subject, PathType.Subject)
        if isNotNull clip then
            c.AddPaths(clip, PathType.Clip)
        c.ExecutePolyTree(clipType, fillRule)
        |> fst // get closed paths from tuple result, ignoring open paths


    let rec private recursivelyAddPolyNodeToPaths (polyPath: PolyPath64<'Z>) (paths: Paths64<'Z>) : unit =
        if isNotNull polyPath.Poly && polyPath.Poly.PointCount > 0 then
            paths.Add(polyPath.Poly)
        for i = 0 to polyPath.Count - 1 do
            recursivelyAddPolyNodeToPaths (polyPath.Child(i)) paths

    /// Flattens a `PolyTree64` structure into a plain `Paths64<'Z>` list of contours.
    let polyTreeToPaths64 (polyTree:PolyTree64<'Z>) : Paths64<'Z> =
        let result = Paths64<'Z>()
        for i = 0 to polyTree.Count - 1 do
            recursivelyAddPolyNodeToPaths (polyTree.Child(i)) result
        result




// #endregion
// #region Path64 module


/// Utility functions for
/// creating new Path64 instances from various input formats and
/// working with Path64 structures, such as mapping and iterating over coordinates and Z values.
module Path64 =



    /// Creates an empty Path64 with no points.
    /// And not accepting Z values.
    let inline createEmpty() : Path64<unit> =
        Path64<unit>(ResizeArray<float>(), None)

    /// Creates an empty Path64 with no points and with Z values enabled.
    let inline createEmptyZ<'Z>() : Path64<'Z> =
        Path64<'Z>(ResizeArray<float>(), Some (ResizeArray<'Z>()))

    /// Creates a Path64 using the ResizeArray of interleaved X and Y coordinates.
    /// Coordinates are stored as floats and are not rounded.
    /// The provided ResizeArray is used directly without copying, so it should not be modified after being passed in.
    /// And not accepting Z values.
    let inline createDirectly (xys:ResizeArray<float>) : Path64<unit> =
        Path64<unit>(xys, None)


    /// Creates a Path64 using the ResizeArray of interleaved X and Y coordinates, and a ResizeArray of Z values.
    /// The provided ResizeArrays are used directly without copying, so they should not be modified after being passed in.
    let inline createDirectlyZ (zs:ResizeArray<'Z>) (xys:ResizeArray<float>) : Path64<'Z> =
        Path64<'Z>(xys, Some zs)


    /// Creates a new Path64 using the ResizeArray of interleaved X and Y coordinates.
    /// The provided ResizeArray is copied into a new ResizeArray.
    /// Coordinates are stored as floats and are not rounded.
    let inline createFrom (xys:ResizeArray<float>) : Path64<unit> =
        Path64<unit>(xys.GetRange(0, xys.Count), None)

    /// Creates a new Path64 using the ResizeArray of interleaved X and Y coordinates.
    /// The provided ResizeArray is copied into a new ResizeArray.
    /// The provided ResizeArray of Z values is used directly without copying, so it should not be modified after being passed in.
    let inline createFromZ (zs:ResizeArray<'Z>) (xys:ResizeArray<float>) : Path64<'Z> =
        Path64<'Z>(xys.GetRange(0, xys.Count), Some zs)

    /// Creates a new Path64 using the seq of interleaved X and Y coordinates.
    /// The provided seq copied into a new ResizeArray.
    let inline createFromSeq (xys:seq<float>) : Path64<unit> =
        Path64<unit>(ResizeArray xys  , None)

    /// Creates a new Path64 using the seq of interleaved X and Y coordinates.
    /// The provided seq copied into a new ResizeArray.
    /// The provided seq of Z values is shallow copied.
    let inline createFromSeqZ (zs:seq<'Z>) (xys:seq<float>) : Path64<'Z> =
        Path64<'Z>( ResizeArray xys , Some (ResizeArray(zs)))


    /// Creates a Path64 from a seq (IEnumerable) of objects with X and Y members (UPPERCASE).
    /// The provided points are copied into a new ResizeArray.
    let inline createFromXYMembers (xyObjs: seq<^T> ) : Path64<unit> = // when ^T: (member X:_) and ^T: (member Y:_)
        let coords = ResizeArray<float>()
        for pt in xyObjs do
            let x = (^T:(member X:_) pt)
            let y = (^T:(member Y:_) pt)
            coords.Add (float x)
            coords.Add (float y)
        Path64<unit>(coords, None)

    /// Creates a Path64 from a seq (IEnumerable) of objects with x and y members (lowercase).
    /// The provided points are copied into a new ResizeArray.
    let inline createFromxyMembers (xyObjs: seq<^T>) : Path64<unit> = //when ^T: (member X:_) and ^T: (member Y:_)
        let coords = ResizeArray<float>()
        for pt in xyObjs do
            let x = (^T:(member x:_) pt)
            let y = (^T:(member y:_) pt)
            coords.Add (float x)
            coords.Add (float y)
        Path64<unit>(coords, None)

    /// Enables Z values for this path, initializing them to the default value of 'Z.
    /// Returns a new Path64 with using same vertices array (without copying) and Z values initialized to the default value of 'Z.
    let inline enableZ<'Z>(p: Path64<unit>) : Path64<'Z>=
        if p.HasZs then
            invalidOp "Path64.enableZ: path already has Z values."
        let pointCount =  Rarr.len p.XYs / 2
        let newZs = ResizeArray<'Z>(pointCount)
        for i = 0 to pointCount - 1 do
            newZs.Add (Null.DEFZ())
        Path64<'Z>(p.XYs, Some newZs)


    /// Enables Z values for this path with the provided ResizeArray of Z values.
    /// The provided ResizeArray must have the same number of elements as the point count.
    /// Returns a new Path64 using the same vertices array (without copying) and the provided Z values.
    /// Fails if Z values are already enabled, or if the provided ResizeArray has the wrong number of elements.
    let inline enableZWith (zs: ResizeArray<'Z>) (p: Path64<unit>) : Path64<'Z> =
        if p.HasZs then
            invalidOp "Path64.enableZWith: path already has Z values."
        let pointCount = Rarr.len p.XYs / 2
        if zs |> Rarr.len <> pointCount then
            raise (ArgumentException $"Path64.enableZWith: zs.Count ({zs.Count}) <> point count ({pointCount})")
        Path64<'Z>(p.XYs, Some zs)


    /// Applies a mapping function to the X and Y coordinates of each point in the path, returning a new Path64 with the same Z values.
    let inline mapXY (mapping: float -> float) (p: Path64<'Z>) : Path64<'Z> =
        let newXys = p.XYs |> Rarr.map mapping
        Path64<'Z>(newXys, p.Zs)

    /// Iterates over the X and Y coordinates of each point in the path, applying a function to them.
    let inline iterXY (f: float -> unit) (p: Path64<'Z>) : unit =
        let xys = p.XYs
        for i = 0 to xys |> Rarr.lastIdx do
            f xys[i]

    /// Applies a mapping function to the Z value of each point in the path, returning a new Path64 with the same X and Y coordinates array.
    /// If the path does not have Z values, this function returns a new Path64 with the same X and Y coordinates and no Z values.
    let inline mapZ (mapping: 'Z -> 'U) (p: Path64<'Z>) : Path64<'U> =
        match p.Zs with
        | Some zs ->
            let newZs = zs |> Rarr.map mapping
            Path64<'U>(p.XYs, Some newZs)
        | None ->
            Path64<'U>(p.XYs, None)

    /// Iterates over the Z value of each point in the path, applying a function to them.
    let inline iterZ (f: 'Z -> unit) (p: Path64<'Z>) : unit =
        match p.Zs with
        | Some zs ->
            for i = 0 to zs |> Rarr.lastIdx do
                f zs[i]
        | None -> ()

    /// Returns a new Path64 with the order of the vertices and Z values if present reversed.
    let reversePath (p: Path64<'Z>) : Path64<'Z> =
        Geo.reversePath p

    /// Checks if the path has a positive orientation.
    /// That means, if the signed area of the path is positive.
    /// That means a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    /// Also returns `true` for degenerate paths with zero area.
    let hasPositiveOrientation (p: Path64<'Z>) : bool =
        p.SignedArea >= 0.0


    /// Checks if the path has a positive orientation.
    /// That means, if the signed area of the path is positive.
    /// That means a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    /// Returns `false` for degenerate paths with zero area, as they are not considered to have a valid orientation.
    let isCounterClockwise (p: Path64<'Z>) =
        p.SignedArea > 0.0


    /// Ensures that the path has a positive orientation by checking its signed area and reversing it if necessary.
    /// A positive orientation is a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let ensurePositiveOrientation (p: Path64<'Z>) : Path64<'Z> =
        if hasPositiveOrientation p then
            p
        else
            reversePath p


    /// Ensures that the path has a negative orientation by checking its signed area and reversing it if necessary.
    /// A negative orientation is a clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let ensureNegativeOrientation (p: Path64<'Z>) : Path64<'Z> =
        if hasPositiveOrientation p then
            reversePath p
        else
            p

    /// Returns the signed area of this path.
    /// Paths with negative area (clockwise orientation) are subtracted,
    /// so holes reduce the total. The result can be negative.
    let signedArea (ps: Path64<'Z>) : float =
        ps.SignedArea

    /// Returns the absolute area of this path, which is always positive regardless of the path's orientation.
    let absArea (ps: Path64<'Z>) : float =
        abs ps.SignedArea


// #endregion
// #region Paths64 module

/// Utility functions for creating Paths64 collections.
/// A Paths64 is just a ResizeArray of Path64, but these functions can help with creating them from various input formats.
module Paths64 =

    /// Returns the total signed area of all paths (the sum of each path's signed area).
    /// Paths with negative area (clockwise orientation) are subtracted,
    /// so holes reduce the total. The result can be negative.
    let signedArea (ps: Paths64<'Z>) : float =
        let mutable totalArea = 0.0
        for i = 0 to ps.Count - 1 do
            totalArea <- totalArea + (Rarr.getIdx i ps).SignedArea
        totalArea

    /// Applies a mapping function to the X and Y coordinates of each point in every path, returning a new Paths64 with the same Z values.
    let inline mapXY (mapping: float -> float) (p: Paths64<'Z>) : Paths64<'Z> =
        Paths64<'Z>(p |> Rarr.map (Path64.mapXY mapping))

    /// Iterates over the X and Y coordinates of each point in every path, applying a function to them.
    let inline iterXY (f: float -> unit) (p: Paths64<'Z>) : unit =
        p |> Rarr.iter (Path64.iterXY f)

    /// Applies a mapping function to the Z value of every point in every path, returning a new Paths64 with the same X and Y coordinate arrays.
    /// Paths without Z values are returned with the same X and Y coordinates and no Z values.
    let inline mapZ (mapping: 'Z -> 'U) (ps: Paths64<'Z>) : Paths64<'U> =
        Paths64<'U>(ps |> Rarr.map (Path64.mapZ mapping))

    /// Iterates over the Z value of every point in every path, applying a function to them.
    let inline iterZ (f: 'Z -> unit) (ps: Paths64<'Z>) : unit =
        ps |> Rarr.iter (Path64.iterZ f)

    /// Creates an empty Paths64 with no paths.
    /// And not accepting Z values.
    let inline createEmpty() : Paths64<unit> =
        ResizeArray<Path64<unit>>()

    /// Creates an empty Paths64 whose path type carries Z values.
    let inline createEmptyZ<'Z>() : Paths64<'Z> =
        ResizeArray<Path64<'Z>>()

    /// Creates a Paths64 containing a single Path64 from a seq of interleaved X and Y coordinates.
    /// The provided coordinates are copied into a new ResizeArray.
    /// And not accepting Z values.
    let createSingle (xys: seq<float>) : Paths64<unit> =
        let ps = Paths64<unit>()
        ps.Add(Path64.createFromSeq xys)
        ps

    /// Creates a Paths64 containing a single Path64 from a seq of interleaved X and Y coordinates and a seq of Z values.
    /// The provided coordinates and Z values are copied into new ResizeArrays.
    let createSingleZ (zs: seq<'Z>) (xys: seq<float>) : Paths64<'Z> =
        let ps = Paths64<'Z>()
        ps.Add(Path64.createFromSeqZ zs xys)
        ps

    /// Creates a Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates.
    /// Coordinates are stored as floats and are not rounded.
    /// The provided ResizeArrays are used directly without copying, so they should not be modified after being passed in.
    /// And not accepting Z values.
    let inline createDirectly (xyss:ResizeArray<ResizeArray<float>>) : Paths64<unit> =
        Paths64<unit>(xyss |> Rarr.map( fun xys -> Path64.createDirectly xys))

    /// Creates a Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates, and a ResizeArray of ResizeArrays of Z values.
    /// Coordinates are stored as floats and are not rounded.
    /// If these ResizeArrays don't have the same number of elements, an exception is thrown.
    /// The provided ResizeArrays are used directly without copying, so they should not be modified after being passed in.
    let inline createDirectlyZ (zss:ResizeArray<ResizeArray<'Z>>) (xyss:ResizeArray<ResizeArray<float>>) : Paths64<'Z> =
        if  Rarr.len zss <> Rarr.len xyss then
            raise (ArgumentException $"createDirectlyZ: zss.Count ({zss.Count}) <> xyss.Count ({xyss.Count})")
        let ps = ResizeArray<Path64<'Z>>(xyss |> Rarr.len)
        for i = 0 to xyss |> Rarr.lastIdx do
            ps.Add (Path64<'Z>(xyss[i], Some zss[i]))
        Paths64<'Z>(ps)


    /// Creates a new Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates.
    /// The provided ResizeArray is copied into a new ResizeArray.
    let inline createFrom (xyss:ResizeArray<ResizeArray<float>>) : Paths64<unit> =
        Paths64<unit>(xyss |> Rarr.map( fun xys -> Path64.createFrom xys))

    /// Creates a new Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates and Z values.
    /// The XY coordinate buffers are copied. The provided ResizeArrays of Z values are used directly without copying, so they should not be modified after being passed in.
    let inline createFromZ  (xyss:ResizeArray<ResizeArray<float> * ResizeArray<'Z>>) : Paths64<'Z> =
        let ps = ResizeArray<Path64<'Z>>(xyss |> Rarr.len)
        for i = 0 to xyss |> Rarr.lastIdx do
            let xys, zs = xyss[i]
            ps.Add (Path64.createFromZ zs xys)
        Paths64<'Z>(ps)

    /// Creates a new Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates.
    /// The provided ResizeArray is copied into a new ResizeArray.
    let inline createFromSeq (xyss:seq<#seq<float>>) : Paths64<unit> =
        let ps = ResizeArray<Path64<unit>>()
        for xys in xyss do
            ps.Add (Path64.createFromSeq xys)
        Paths64<unit>(ps)

    /// Creates a new Paths64 using seqs of interleaved X and Y coordinates and Z values.
    /// The provided coordinate and Z sequences are copied into new ResizeArrays.
    let inline createFromSeqZ  (xyss:seq<#seq<float> * #seq<'Z>>) : Paths64<'Z> =
        let ps = ResizeArray<Path64<'Z>>()
        for xys, zs in xyss do
            ps.Add (Path64.createFromSeqZ zs xys)
        Paths64<'Z>(ps)


    /// Creates a Paths64 from a seq (IEnumerable) of objects with X and Y members (UPPERCASE).
    /// The provided points are copied into new ResizeArrays.
    let inline createFromXYMembers (xyObjss: seq<#seq<'T>> ) : Paths64<unit> = // when ^T: (member X:_) and ^T: (member Y:_)
        let ps = ResizeArray<Path64<unit>>()
        for xys in xyObjss do
            ps.Add (Path64.createFromXYMembers xys)
        Paths64<unit>(ps)

    /// Creates a Paths64 from a seq (IEnumerable) of objects with x and y members (lowercase).
    /// The provided points are copied into new ResizeArrays.
    let inline createFromxyMembers (xyObjss: seq<#seq<'T>> ) : Paths64<unit> = // when ^T: (member x:_) and ^T: (member y:_)
        let ps = ResizeArray<Path64<unit>>()
        for xys in xyObjss do
            ps.Add (Path64.createFromxyMembers xys)
        Paths64<unit>(ps)


    /// Enables Z values for this path, initializing them to the default value of 'Z.
    /// Returns a new Paths64 with using same vertices array (without copying) and Z values initialized to the default value of 'Z.
    let inline enableZ<'Z>(p: Paths64<unit>) : Paths64<'Z>=
        Rarr.map Path64.enableZ<'Z> p


    /// Enables Z values for this path with the provided ResizeArrays of Z values.
    /// The provided ResizeArrays must have the same number of elements as the point count.
    /// Returns a new Paths64 using the same vertices array (without copying) and the provided Z values.
    /// Fails if Z values are already enabled, or if the provided ResizeArray has the wrong number of elements.
    let inline enableZWith (zs: ResizeArray<ResizeArray<'Z>>) (ps: Paths64<unit>) : Paths64<'Z> =
        if zs.Count <> ps.Count then
            raise (ArgumentException $"Paths64.enableZWith: zs.Count ({zs.Count}) <> paths count ({ps.Count})")
        let rs = new ResizeArray<Path64<'Z>>(ps.Count)
        for i = 0 to ps |> Rarr.lastIdx do
            let p = Rarr.getIdx i ps
            if p.HasZs then
                invalidOp "Paths64.enableZWith: path already has Z values."
            let z = zs[i]
            let pointCount = Rarr.len p.XYs / 2
            if z |> Rarr.len <> pointCount then
                raise (ArgumentException $"Paths64.enableZWith: zs.Count ({z.Count}) <> point count ({pointCount})")
            rs.Add (Path64<'Z>(p.XYs, Some z))
        Paths64<'Z>(rs)



    /// Returns a new Paths64 with the order of the vertices and Z values if present reversed in each sub Path64
    let reversePaths (ps: Paths64<'Z>) : Paths64<'Z> =
        ps |> Rarr.map Path64.reversePath

    /// Checks if all paths have a positive orientation.
    /// That means, if the signed area of the path is positive.
    /// That means a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    /// Also returns `true` for degenerate paths with zero area.
    let havePositiveOrientation (ps: Paths64<'Z>) : bool =
        ps |> Seq.forall Path64.hasPositiveOrientation


    /// Checks if all paths have a positive orientation.
    /// That means, if the signed area of the path is positive.
    /// That means a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    /// Returns `false` for degenerate paths with zero area, as they are not considered to have a valid orientation.
    let areCounterClockwise (ps: Paths64<'Z>) =
        ps |> Seq.forall Path64.isCounterClockwise


    /// Ensures that all paths have a positive orientation by checking their signed area and reversing them if necessary.
    /// Returns a new Paths64; the input list is not modified.
    /// Paths that already have a positive orientation are reused, the others are reversed copies.
    /// A positive orientation is a counter-clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let ensurePositiveOrientations (ps: Paths64<'Z>) : Paths64<'Z> =
        let rs = Paths64<'Z>(ps.Count)
        for i = 0 to ps.Count - 1 do
            let s = Rarr.getIdx i ps
            if Path64.hasPositiveOrientation s then
                rs.Add s
            else
                rs.Add(Path64.reversePath s)
        rs


    /// Ensures that all paths have a negative orientation by checking their signed area and reversing them if necessary.
    /// Returns a new Paths64; the input list is not modified.
    /// Paths that already have a negative orientation are reused, the others are reversed copies.
    /// A negative orientation is a clockwise loop when the global Y-axis is positive upwards.(Right handed coordinate system)
    let ensureNegativeOrientations (ps: Paths64<'Z>) : Paths64<'Z> =
        let rs = Paths64<'Z>(ps.Count)
        for i = 0 to ps.Count - 1 do
            let s = Rarr.getIdx i ps
            if Path64.hasPositiveOrientation s then
                rs.Add(Path64.reversePath s)
            else
                rs.Add s
        rs