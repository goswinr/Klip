(*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  14 December 2025                                                *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  This module contains simple functions that will likely cover    *
*              most polygon boolean and offsetting needs, while also avoiding  *
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
open Klip.KlipInternal


// #region Klipper

/// High-level convenience wrappers over Clipper64
/// Clipping operations will always return Positive oriented solutions
/// (unless the Clipper object's ReverseSolution property has been enabled).
/// This means that outer polygon contours will wind anti-clockwise (in Cartesian coordinates),
/// and inner hole contours will wind clockwise. And because paths in clipping solutions never intersect,
/// both NonZero and EvenOdd filling would correctly apply to the solution,
/// though it's usual to apply the same FillRule that was applied to the subject and clip paths during clipping.

/// A lot of effort has gone into returning solutions close to their simplest forms,
/// but there's no way to do this perfectly without significantly degrading performance.
/// So there will, on occasions, be solutions with polygons that are touching.
/// If this is problematic, then a follow up union operation will frequently bring these solutions to their simplest forms
///
/// The library supports open path clipping, and this may also be performed concurrently with closed path clipping.
/// However, only subject paths may be open. Except in union operations,
/// the presence of closed subject paths will have no effect on open path solutions.
/// In union operations, open paths will be clipped wherever they overlap any closed paths
/// (regardless of whether they are subject or clip paths).
module Klipper =

    /// Performs a boolean operation between `subject` and `clip` based on the specified `ClipType` and `FillRule`.
    let booleanOp (clipType:ClipType, subject:Paths64<'Z>, clip:Paths64<'Z>, fillRule:FillRule, zCallback:ZCallback64<'Z> option) : Paths64<'Z> =
        let solution = Paths64<'Z>()
        if isNull' subject then
            solution
        else
            let c = Clipper64()
            c.ZCallback <- zCallback
            c.AddPaths(subject, PathType.Subject)
            if isNotNull clip then
                c.AddPaths(clip, PathType.Clip)
            c.Execute(clipType, fillRule, solution) |> ignore
            solution

    /// Performs the intersection operation between `subject` and `clip` using the NonZero fill rule.
    let intersect (clip:Paths64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Intersection, subject, clip, FillRule.NonZero, None)

    /// Performs the intersection operation between `subject` and `clip` using the NonZero fill rule and a custom Z callback.
    let intersectZ (zCallback:ZCallback64<'Z>) (clip:Paths64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Intersection, subject, clip, FillRule.NonZero, Some zCallback)

    /// Performs the union operation between `subject` and `clip` using the NonZero fill rule.
    let union (clip:Paths64<'Z>) (subject:Paths64<'Z>)  : Paths64<'Z> =
        booleanOp (ClipType.Union, subject, clip, FillRule.NonZero, None)

    /// Performs the union operation between `subject` and `clip` using the NonZero fill rule and a custom Z callback.
    let unionZ (zCallback:ZCallback64<'Z>) (clip:Paths64<'Z>) (subject:Paths64<'Z>)  : Paths64<'Z> =
        booleanOp (ClipType.Union, subject, clip, FillRule.NonZero, Some zCallback)


    /// Performs the union operation on a single subject path, resolving self-intersections.
    let unionSelf (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Union, subject, null, FillRule.NonZero, None)

    /// Performs the union operation on a single subject path, resolving self-intersections, with a custom Z callback.
    let unionSelfZ (zCallback:ZCallback64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Union, subject, null, FillRule.NonZero, Some zCallback)

    /// Performs the difference operation (subject regions that are not in the clip region) using the NonZero fill rule.
    let difference (clip:Paths64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Difference, subject, clip, FillRule.NonZero, None)


    /// Performs the difference operation (subject regions that are not in the clip region) using the NonZero fill rule and a custom Z callback.
    let differenceZ (zCallback:ZCallback64<'Z>) (clip:Paths64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Difference, subject, clip, FillRule.NonZero, Some zCallback)

    /// Performs the XOR operation (regions in either subject or clip but not both) using the NonZero fill rule.
    let xor (clip:Paths64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Xor, subject, clip, FillRule.NonZero, None)

    /// Performs the XOR operation (regions in either subject or clip but not both) using the NonZero fill rule and a custom Z callback.
    let xorZ (zCallback:ZCallback64<'Z>) (clip:Paths64<'Z>) (subject:Paths64<'Z>) : Paths64<'Z> =
        booleanOp (ClipType.Xor, subject, clip, FillRule.NonZero, Some zCallback)



    /// Performs a boolean operation between `subject` and `clip` based on the specified `ClipType` and `FillRule`,
    /// returning the result as a `PolyTree64` which preserves the parent-child contour relationships.
    let booleanOpWithPolyTree( clipType:ClipType,subject:Paths64<'Z>, clip:Paths64<'Z>, polyTree:PolyTree64<'Z>, fillRule:FillRule, zCallback:ZCallback64<'Z> option) : unit =
        if isNotNull subject then
            let c = Clipper64()
            c.ZCallback <- zCallback
            c.AddPaths(subject, PathType.Subject)
            if isNotNull clip then
                c.AddPaths(clip, PathType.Clip)
            c.Execute(clipType, fillRule, polyTree) |> ignore

    let rec private _addPolyNodeToPaths (polyPath: PolyPath64<'Z>) (paths: Paths64<'Z>) : unit =
        if isNotNull polyPath.Poly && polyPath.Poly.PointCount > 0 then
            paths.Add(polyPath.Poly)
        for i = 0 to polyPath.Count - 1 do
            _addPolyNodeToPaths (polyPath.Child(i)) paths

    /// Flattens a `PolyTree64` structure into a plain `Paths64<'Z>` list of contours.
    let polyTreeToPaths64 (polyTree:PolyTree64<'Z>) : Paths64<'Z> =
        let result = Paths64<'Z>()
        for i = 0 to polyTree.Count - 1 do
            _addPolyNodeToPaths (polyTree.Child(i)) result
        result

    // #endregion
    // #region Offsetting

    /// Same as `Klipper.inflatePaths`.
    /// Offsets the given closed polygons.
    /// Inflates with positive `delta` or deflates with negative `delta`.
    /// `joinType` controls how corners are constructed (`Miter`, `Square`, `Bevel`, `Round`).
    /// `miterLimit` limits how far miter joins extend (typical default 2.0).
    /// `arcTolerance` controls arc smoothness when using `Round` joins;
    /// 0.0 selects an automatic tolerance of `|delta| / 500`.
    let offsetPaths (paths:Paths64<'Z>, delta:float, joinType:JoinType, miterLimit:float, arcTolerance:float) : Paths64<'Z> =
        let solution = Paths64<'Z>()
        if isNull' paths then
            solution
        else
            let co = ClipperOffset<'Z>(miterLimit, arcTolerance)
            co.AddPaths(paths, joinType, EndType.Polygon) // EndType.Polygon makes the path closed and only offsets to one side,
            co.Execute(delta, solution)
            solution


    /// Same as `offsetPaths`
    let inflatePaths (paths:Paths64<'Z>, delta:float, joinType:JoinType, miterLimit:float, arcTolerance:float) : Paths64<'Z> =
        offsetPaths(paths, delta, joinType, miterLimit, arcTolerance)


    /// Same as `Klipper.inflate`.
    /// Offsets the given closed polygons. Inflates with positive `delta` or deflates with negative `delta`.
    /// Uses miter joins, a miter limit of 2.0, and the default arc tolerance.
    let offset (delta:float) (paths:Paths64<'Z>) : Paths64<'Z> =
        offsetPaths(paths, delta, JoinType.Miter, 2.0, 0.0)


    /// Same as `offset`
    let inflate (delta:float) (paths:Paths64<'Z>) : Paths64<'Z> =
        offset delta paths

    /// Same as `Klipper.inflateOpenPaths`.
    /// Offsets open paths (lines) by `delta`, with the specified join type for corners and end type for line ends.
    /// The provide paths are considered open and offset to both sides of the polyline and looped around the ends of the line.
    /// (unless EndType.Joined is used, in which case the ends of both offsets are joined instead of looped around).
    /// Basically creating a polyline with a thickness.
    /// <param name="delta">The distance to offset.</param>
    /// <param name="joinType">The type of join to use for corners (Miter, Square, Bevel, Round).</param>
    /// <param name="endType">The type of end to use for line ends (Butt, Square or Round) for open paths.
    /// EndType.Joined if the ends should be joined instead of looped around).
    /// EndType.Polygon would only be valid for offsetting to one side.</param>
    /// <param name="miterLimit">The maximum distance in multiples of delta that vertices can be offset from their original positions before squaring is applied.</param>
    /// <param name="arcTolerance">The tolerance for arc approximation when using Round joins.</param>
    let offsetBothSides(paths:Paths64<'Z>, delta:float, joinType:JoinType, endType:EndType, miterLimit:float, arcTolerance:float) : Paths64<'Z> =
        let solution = Paths64<'Z>()
        if isNull' paths then
            solution
        else
            if endType = EndType.Polygon then
                raise (ArgumentException "Klip.offsetBothSides: endType cannot be EndType.Polygon for offsetBothSides.")
            let co = ClipperOffset<'Z>(miterLimit, arcTolerance)
            co.AddPaths(paths, joinType, endType)
            co.Execute(delta, solution)
            solution


    /// Same as `offsetBothSides`
    let offsetOpenPaths (paths:Paths64<'Z>, delta:float, joinType:JoinType, endType:EndType, miterLimit:float, arcTolerance:float) : Paths64<'Z> =
        offsetBothSides(paths, delta, joinType, endType, miterLimit, arcTolerance)


    // #endregion
    // #region Klipper utilities

    /// Returns a new Path64 with the order of the vertices and Z values if present reversed.
    let reversePath (p: Path64<'Z>) : Path64<'Z> =
        OffsetInternal.reversePath p

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
    let ensurePositiveOrientation (p: Path64<'Z>) : Path64<'Z> =
        if hasPositiveOrientation p then
            p
        else
            reversePath p





    // #endregion
    // #region Klipper utilities

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
    let ensurePositiveOrientation (p: Path64<'Z>) : Path64<'Z> =
        if hasPositiveOrientation p then
            p
        else
            reversePath p





//#endregion
//#region Floats

/// Utility functions for rounding and scaling coordinates.
/// ( ResizeArrays of floats representing interleaved X and Y coordinates).
module Floats =


    /// Multiplies all points in the provided ResizeArray by the given factor, and rounds to integers.
    /// Returns a new ResizeArray containing the scaled and rounded coordinates, without modifying the original ResizeArray.
    let inline scaleUpAndRound (scaleFactor:float) (xys:ResizeArray<float>) : ResizeArray<float> =
        xys |> Rarr.map (fun v -> Geo.jsRound(v * scaleFactor))


    /// Multiplies all points in the provided ResizeArray by the given factor, and rounds to integers.
    /// Returns the same ResizeArray that was passed in, after modifying it in place.
    let inline scaleUpAndRoundInPlace (scaleFactor:float) (xys:ResizeArray<float>) : ResizeArray<float> =
        for i = 0 to xys |> Rarr.lastIdx do
            xys[i] <- Geo.jsRound(xys[i] * scaleFactor)
        xys

    /// Rounds all points in the provided ResizeArray to integers.
    /// Returns a new ResizeArray containing the rounded coordinates, without modifying the original ResizeArray.
    let inline round (xys:ResizeArray<float>) : ResizeArray<float> =
        xys |> Rarr.map Geo.jsRound

    /// Rounds all points in the provided ResizeArray to integers.
    /// Returns the same ResizeArray that was passed in, after modifying it in place.
    let inline roundInPlace (xys:ResizeArray<float>) : ResizeArray<float> =
        for i = 0 to xys |> Rarr.lastIdx do
            xys[i] <- Geo.jsRound(xys[i])
        xys


    /// Divides all points in the provided ResizeArray by the given factor, without rounding.
    /// Returns a new ResizeArray containing the scaled coordinates, without modifying the original ResizeArray.
    let inline scaleDownWithoutRounding (scaleFactor:float) (xys:ResizeArray<float>) : ResizeArray<float> =
       let f = 1.0 / scaleFactor
       xys |> Rarr.map (fun v -> v * f)

    /// Divides all points in the provided ResizeArray by the given factor, without rounding.
    /// Returns the same ResizeArray that was passed in, after modifying it in place.
    let inline scaleDownWithoutRoundingInPlace (scaleFactor:float) (xys:ResizeArray<float>) : ResizeArray<float> =
        let f = 1.0 / scaleFactor
        for i = 0 to xys |> Rarr.lastIdx do
            xys[i] <- xys[i] * f
        xys

//#endregion
//#region Path64 module


/// Utility functions for
/// creating new Path64 instances from various input formats and
/// working with Path64 structures, such as mapping and iterating over coordinates and Z values.
module Path64 =


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



    /// Creates an empty Path64 with no points.
    /// And not accepting Z values.
    let inline createEmpty() : Path64<unit> =
        Path64<unit>(ResizeArray<float>(), None)

    /// Creates an empty Path64 with no points.
    /// And not accepting Z values.
    let inline createEmptyZ<'Z>() : Path64<'Z> =
        Path64<'Z>(ResizeArray<float>(), Some (ResizeArray<'Z>()))

    /// The floats in the provided ResizeArray must be rounded to integers already.
    /// To do rounding at creation, use Path64.createFrom(xys) instead.
    /// Creates a Path64 using the ResizeArray of interleaved X and Y coordinates.
    /// The provided ResizeArray is used directly without copying, so it should not be modified after being passed in.
    /// And not accepting Z values.
    let inline createDirectly (xys:ResizeArray<float>) : Path64<unit> =
        Path64<unit>(xys, None)

    /// The floats in the provided ResizeArray must be rounded to integers already.
    /// To do rounding at creation, use Path64.createFrom(xys) instead.
    /// Creates a Path64 using the ResizeArray of interleaved X and Y coordinates, and a ResizeArray of Z values.
    /// The provided ResizeArrays are used directly without copying, so they should not be modified after being passed in.
    let inline createDirectlyZ (zs:ResizeArray<'Z>) (xys:ResizeArray<float>) : Path64<'Z> =
        Path64<'Z>(xys, Some zs)


    /// Creates a new Path64 using the ResizeArray of interleaved X and Y coordinates.
    /// The provided ResizeArray is first scaled up and rounded to integers, then copied into a new ResizeArray.
    let inline createFrom (scaleFactor:float) (xys:ResizeArray<float>) : Path64<unit> =
        Path64<unit>(Floats.scaleUpAndRound scaleFactor xys, None)

    /// Creates a new Path64 using the ResizeArray of interleaved X and Y coordinates.
    /// The provided ResizeArray is first scaled up and rounded to integers, then copied into a new ResizeArray.
    /// The provided ResizeArray of Z values is used directly without copying, so it should not be modified after being passed in.
    let inline createFromZ (scaleFactor:float) (zs:ResizeArray<'Z>) (xys:ResizeArray<float>) : Path64<'Z> =
        Path64<'Z>(Floats.scaleUpAndRound scaleFactor xys, Some zs)


    /// Creates a new Path64 using the seq of interleaved X and Y coordinates.
    /// The provided seq is first scaled up and rounded to integers, then copied into a new ResizeArray.
    let inline createFromSeq (scaleFactor:float) (xys:seq<float>) : Path64<unit> =
        Path64<unit>(ResizeArray xys |> Floats.scaleUpAndRound scaleFactor , None)

    /// Creates a new Path64 using the seq of interleaved X and Y coordinates.
    /// The provided seq is first scaled up and rounded to integers, then copied into a new ResizeArray.
    /// The provided seq of Z values is shallow copied.
    let inline createFromSeqZ (scaleFactor:float) (zs:seq<'Z>) (xys:seq<float>) : Path64<'Z> =
        Path64<'Z>( ResizeArray xys |> Floats.scaleUpAndRound scaleFactor, Some (ResizeArray(zs)))


    /// Creates a Path64 from a seq (IEnumerable) of objects with X and Y members (UPPERCASE).
    /// The provided points are scaled up and rounded to integers
    let inline createFromXYMembers (scaleFactor:float) (xyObjs: seq<^T> ) : Path64<unit> = // when ^T: (member X:_) and ^T: (member Y:_)
        let coords = ResizeArray<float>(Seq.length xyObjs * 2)
        for pt in xyObjs do
            let x = (^T:(member X:_) pt)
            let y = (^T:(member Y:_) pt)
            coords.Add (float x)
            coords.Add (float y)
        Path64<unit>(Floats.scaleUpAndRound scaleFactor coords, None)

    /// Creates a Path64 from a seq (IEnumerable) of objects with x and y members (lowercase).
    /// The provided points are scaled up and rounded to integers
    let inline createFromxyMembers (scaleFactor:float) (xyObjs: seq<^T>) : Path64<unit> = //when ^T: (member X:_) and ^T: (member Y:_)
        let coords = ResizeArray<float>(Seq.length xyObjs * 2)
        for pt in xyObjs do
            let x = (^T:(member x:_) pt)
            let y = (^T:(member y:_) pt)
            coords.Add (float x)
            coords.Add (float y)
        Path64<unit>(Floats.scaleUpAndRound scaleFactor coords, None)

    /// Enables Z values for this path, initializing them to the default value of 'Z.
    /// Returns a new Path64 with using same vertices array (without copying) and Z values initialized to the default value of 'Z.
    let inline enableZ<'Z>(p: Path64<unit>) : Path64<'Z>=
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
        let pointCount = Rarr.len p.XYs / 2
        if zs |> Rarr.len <> pointCount then
            raise (ArgumentException $"EnableAndInitZ: zs.Count ({zs.Count}) <> point count ({pointCount})")
        Path64<'Z>(p.XYs, Some zs)


    /// Creates a new Path64 with all coordinates multiplied a by the given factor and rounded to integers.
    let inline scaleUp (scaleFactor:float) (p: Path64<'Z>) : Path64<'Z> =
        let newXys = p.XYs |> Floats.scaleUpAndRound scaleFactor
        Path64<'Z>(newXys, p.Zs)

    /// Creates a new Path64 with all coordinates divided by the given factor, without rounding.
    let inline scaleDown (scaleFactor:float) (p: Path64<'Z>) : Path64<'Z> =
        let newXys = p.XYs |> Floats.scaleDownWithoutRounding scaleFactor
        Path64<'Z>(newXys, p.Zs)



//#endregion
//#region Paths64 module

/// Utility functions for creating Paths64 collections.
/// A Paths64 is just a ResizeArray of Path64, but these functions can help with creating them from various input formats.
module Paths64 =


    /// Applies a mapping function to the X and Y coordinates of each point in the path, returning a new Path64 with the same Z values.
    let inline mapXY (mapping: float -> float) (p: Paths64<'Z>) : Paths64<'Z> =
        Paths64<'Z>(p |> Rarr.map (Path64.mapXY mapping))

    /// Iterates over the X and Y coordinates of each point in the path, applying a function to them.
    let inline iterXY (f: float -> unit) (p: Paths64<'Z>) : unit =
        p |> Rarr.iter (Path64.iterXY f)

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

    /// Creates an empty Paths64 with no points.
    /// And not accepting Z values.
    let inline createEmpty() : Paths64<unit> =
        ResizeArray<Path64<unit>>()

    /// Creates an empty Path64 with no points.
    /// And not accepting Z values.
    let inline createEmptyZ<'Z>() : Path64<'Z> =
        Path64<'Z>(ResizeArray<float>(), Some (ResizeArray<'Z>()))

    /// The floats in the provided ResizeArray must be rounded to integers already.
    /// To do rounding at creation, use Paths64.createFrom(xyss) instead.
    /// Creates a Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates.
    /// The provided ResizeArrays are used directly without copying, so they should not be modified after being passed in.
    /// And not accepting Z values.
    let inline createDirectly (xyss:ResizeArray<ResizeArray<float>>) : Paths64<unit> =
        Paths64<unit>(xyss |> Rarr.map( fun xys -> Path64.createDirectly xys))

    /// The floats in the provided ResizeArray must be rounded to integers already.
    /// To do rounding at creation, use Paths64.createFrom(xyss) instead.
    /// Creates a Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates, and a ResizeArray of ResizeArrays of Z values.
    /// If these ResizeArrays don't have the same number of elements, an exception is thrown.
    /// The provided ResizeArrays are used directly without copying, so they should not be modified after being passed in.
    let inline createDirectlyZ (zss:ResizeArray<ResizeArray<'Z>>) (xyss:ResizeArray<ResizeArray<float>>) : Paths64<'Z> =
        if  Rarr.len zss <> Rarr.len xyss then
            raise (ArgumentException $"createDirectlyZ: zss.Count ({zss.Count}) <> xyss.Count ({xyss.Count})")
        let ps = ResizeArray<Path64<'Z>>(xyss |> Rarr.len)
        for i = 0 to xyss |> Rarr.lastIdx do
            ps.Add (Path64<'Z>(xyss[i], Some zss[i]))
        Paths64<'Z>(ps)


    /// Creates a Paths64 from a ResizeArrays of interleaved X and Y coordinates.
    /// And not accepting Z values.
    let inline createSingle (scaleFactor:float) (xys: ResizeArray<float>) : Paths64<unit> =
        let r = ResizeArray<Path64<unit>>()
        r.Add (Path64.createFrom scaleFactor xys)
        r

    /// Creates a Paths64 from a ResizeArrays of Z values and interleaved X and Y coordinates.
    let inline createSingleZ (scaleFactor:float) (zs: ResizeArray<'Z>) (xys: ResizeArray<float>) : Paths64<'Z> =
        let r = ResizeArray<Path64<'Z>>()
        r.Add (Path64.createFromZ scaleFactor zs xys)
        r

    /// Creates a new Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates.
    /// The provided ResizeArray is first scaled up and rounded to integers, then copied into a new ResizeArray.
    let inline createFrom (scaleFactor:float) (xyss:ResizeArray<ResizeArray<float>>) : Paths64<unit> =
        Paths64<unit>(xyss |> Rarr.map( fun xys -> Path64.createFrom scaleFactor xys))

    /// Creates a new Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates and Z values.
    /// The provided ResizeArrays are first scaled up and rounded to integers, then copied into a new ResizeArray.
    /// The provided ResizeArrays of Z values are used directly without copying, so it should not be modified after being passed in.
    let inline createFromZ (scaleFactor:float)  (xyss:ResizeArray<ResizeArray<float> * ResizeArray<'Z>>) : Paths64<'Z> =
        let ps = ResizeArray<Path64<'Z>>(xyss |> Rarr.len)
        for i = 0 to xyss |> Rarr.lastIdx do
            let xys, zs = xyss[i]
            ps.Add (Path64.createFromZ scaleFactor zs xys)
        Paths64<'Z>(ps)

    /// Creates a new Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates.
    /// The provided ResizeArray is first scaled up and rounded to integers, then copied into a new ResizeArray.
    let inline createFromSeq (scaleFactor:float) (xyss:seq<#seq<float>>) : Paths64<unit> =
        let ps = ResizeArray<Path64<unit>>()
        for xys in xyss do
            ps.Add (Path64.createFromSeq scaleFactor xys)
        Paths64<unit>(ps)

    /// Creates a new Paths64 using the ResizeArray of ResizeArrays of interleaved X and Y coordinates and Z values.
    /// The provided ResizeArrays are first scaled up and rounded to integers, then copied into a new ResizeArray.
    /// The provided ResizeArrays of Z values are used directly without copying, so it should not be modified after being passed in.
    let inline createFromSeqZ (scaleFactor:float)  (xyss:seq<#seq<float> * #seq<'Z>>) : Paths64<'Z> =
        let ps = ResizeArray<Path64<'Z>>()
        for xys, zs in xyss do
            ps.Add (Path64.createFromSeqZ scaleFactor zs xys)
        Paths64<'Z>(ps)



    /// Enables Z values for this path, initializing them to the default value of 'Z.
    /// Returns a new Paths64 with using same vertices array (without copying) and Z values initialized to the default value of 'Z.
    let inline enableZ<'Z>(p: Paths64<unit>) : Paths64<'Z>=
        Rarr.map Path64.enableZ<'Z> p


    /// Enables Z values for this path with the provided ResizeArrays of Z values.
    /// The provided ResizeArrays must have the same number of elements as the point count.
    /// Returns a new Paths64 using the same vertices array (without copying) and the provided Z values.
    /// Fails if Z values are already enabled, or if the provided ResizeArray has the wrong number of elements.
    let inline enableZWith (zs: ResizeArray<ResizeArray<'Z>>) (ps: Paths64<unit>) : Paths64<'Z> =
        let rs = new ResizeArray<Path64<'Z>>(ps |> Rarr.len)
        for i = 0 to ps |> Rarr.lastIdx do
            let p = ps[i]
            let z = zs[i]
            let pointCount = Rarr.len p.XYs / 2
            if z |> Rarr.len <> pointCount then
                raise (ArgumentException $"Paths64.enableZWith: zs.Count ({z.Count}) <> point count ({pointCount})")
            rs.Add (Path64<'Z>(p.XYs, Some z))
        Paths64<'Z>(rs)


    /// Creates a new Paths64 with all coordinates multiplied a by the given factor and rounded to integers.
    let inline scaleUp (scaleFactor:float) (p: Paths64<'Z>) : Paths64<'Z> =
        p |> Rarr.map (Path64.scaleUp scaleFactor)

    /// Creates a new Paths64 with all coordinates divided by the given factor, without rounding.
    let inline scaleDown (scaleFactor:float) (p: Paths64<'Z>) : Paths64<'Z> =
        p |> Rarr.map (Path64.scaleDown scaleFactor)



    /// Creates a Paths64 from a seq (IEnumerable) of objects with X and Y members (UPPERCASE).
    /// The provided points are scaled up and rounded to integers
    let inline createFromXYMembers (scaleFactor:float) (xyObjss: seq<#seq<'T>> when 'T: (member X:_) and 'T: (member Y:_) ) : Paths64<unit> =
        let ps = ResizeArray<Path64<unit>>()
        for xys in xyObjss do
            ps.Add (Path64.createFromXYMembers scaleFactor xys)
        Paths64<unit>(ps)

    /// Creates a Paths64 from a seq (IEnumerable) of objects with x and y members (lowercase).
    /// The provided points are scaled up and rounded to integers
    let inline createFromxyMembers (scaleFactor:float) (xyObjss: seq<#seq<'T>> when 'T: (member x:_) and 'T: (member y:_) ) : Paths64<unit> =
        let ps = ResizeArray<Path64<unit>>()
        for xys in xyObjss do
            ps.Add (Path64.createFromxyMembers scaleFactor xys)
        Paths64<unit>(ps)
