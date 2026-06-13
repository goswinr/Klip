(*******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  12 December 2025                                                *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  Core structures and functions for the Clipper Library           *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
*******************************************************************************)

// ported to TypeScript at https://github.com/countertype/clipper2-ts
// then ported to F# and simplified here:

namespace Klip

open System

type internal OPT = Runtime.InteropServices.OptionalAttribute
type internal DEF = Runtime.InteropServices.DefaultParameterValueAttribute



[<AutoOpen>]
module internal Operators =

    let inline ( === ) (x: obj) (y: obj) : bool =
        Object.ReferenceEquals(x, y)

    let inline ( =!= ) (x: obj) (y: obj) : bool =
        not (Object.ReferenceEquals(x, y))

// #region Null module

module internal Null =

    let inline isNull' (x: obj) : bool =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr x "$0 === null"
        #else
            x === null
        #endif


    let inline isNotNull (x: obj) : bool =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr x "$0 !== null"
        #else
            x =!= null
        #endif

    /// Needed for cheating the F# compiler to set F# records to null.
    let inline null'() : 'T =
        #if FABLE_COMPILER_JAVASCRIPT // but not for FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr () "null" // to avoid emitting defaultOf() call, but  this seems irrelevant to performance.
        #else // including FABLE_COMPILER_TYPESCRIPT
            Unchecked.defaultof<'T>
        #endif

    let inline opt (x: 'T) : option<'T> =
        if isNull' x then None else Some x


    let inline DEFZ() : 'T =
        #if FABLE_COMPILER_JAVASCRIPT // but not for FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr () "null" // to avoid emitting defaultOf() call, but  this seems irrelevant to performance.
        #else // including FABLE_COMPILER_TYPESCRIPT
            Unchecked.defaultof<'T>
        #endif

// #region Rarr module
[<RequireQualifiedAccess>]
module internal Rarr =


    /// returns resizeArray.Count , but optimized in Fable
    let inline len (resizeArray: ResizeArray<'T>) : int =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr (resizeArray) "$0.length" // avoid call to count() in fable lib
        #else
            resizeArray.Count
        #endif

    /// returns resizeArray.Count - 1 , but optimized in Fable
    let inline lastIdx  (resizeArray: ResizeArray<'T>) : int =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr (resizeArray) "$0.length - 1" // avoid call to count() in fable lib
        #else
            resizeArray.Count - 1
        #endif

    /// this is more efficient than ResizeArray.Clear() in Fable,
    /// which emits .splice(0)
    let inline clear (arr: ResizeArray<'T>) : unit =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsStatement arr "$0.length = 0"
        #else
            arr.Clear()
        #endif

    let inline pop (arr: ResizeArray<'T>) : unit =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsStatement arr "$0.pop()"
        #else
            arr.RemoveAt(arr.Count - 1)
        #endif

    let inline map (mapping: 'T -> 'U) (resizeArray: ResizeArray<'T>) : ResizeArray<'U> =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr (resizeArray, mapping) "$0.map($1)" // this works only because a ResizeArray is never a TypedArray in JS
        #else
            resizeArray.ConvertAll (System.Converter mapping) // would work in Fable too
        #endif

    let inline iter (mapping: 'T -> unit) (resizeArray: ResizeArray<'T>) : unit =
        for i = 0 to resizeArray |> lastIdx do
            mapping resizeArray[i]

    let inline getIdx (i: int) (arr: ResizeArray<'T>) : 'T =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr (arr, i) "$0[$1]"
        #else
            arr[i]
        #endif

    let inline setIdx (i: int) (value: 'T) (arr: ResizeArray<'T>) : unit =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsStatement (arr, i, value) "$0[$1] = $2"
        #else
            arr[i] <- value
        #endif

    let inline intSumBy (mapping: 'T -> int) (resizeArray: ResizeArray<'T>) : int =
        let mutable total = 0
        for i = 0 to resizeArray |> lastIdx do
            total <- total + mapping resizeArray[i]
        total



// #endregion
// #region type Path64


/// Contains a sequence of vertices defining a single contour.
/// The path stores X, Y coordinates in a single flat ResizeArray of floats,
/// interleaved as x0, y0, x1, y1, ...
///
/// Occasionally users may wish to assign user-defined data to vertices,
/// For this you may pass in an optional ResizeArray of objects as Z values to the constructor,
/// and these will be retained if these vertices are returned in clipping solutions.
/// Do not confuse the additional Z member with 3D coordinates.
/// If an optional Z ResizeArray is provided, it must have the same number of elements as the vertex count.
///
/// If no Z values are needed, the type parameter 'Z can be left as unit.
/// Use the static Path64.create... methods to create Path64 instances, which will ensure the correct type is used for Z values.
/// When a Path64 is created without Z values, its type will be Path64<unit>, and the Zs member will be None.
[<AllowNullLiteral>]
type Path64<'Z> ( xys:ResizeArray<float>, zs:option<ResizeArray<'Z>>) =

    do
        if xys.Count % 2 <> 0 then
            raise (ArgumentException $"Path64 constructor: coords.Count ({xys.Count}) must be even")
        match zs with
        |Some zs ->
            let pointCount = xys.Count / 2
            if zs.Count <> pointCount then
                raise (ArgumentException $"Path64 constructor: zs.Count ({zs.Count}) <> point count ({pointCount})")
        |None -> ()



    /// Gets the flat interleaved coordinate buffer of the path.
    member _.XYs : ResizeArray<float> =
        xys

    /// The Z values only contain optional user-defined data, can be any object.
    /// Don't confuse the additional Z member with 3D coordinates.
    /// This is None if no Z values were provided in the constructor,
    /// otherwise it is Some with the ResizeArray of objects.
    member _.Zs : option<ResizeArray<'Z>> =
        zs

    /// Returns true if this path has Z values, false if not.
    member _.HasZs : bool =
        zs.IsSome


    /// Returns true if the path has no points, false otherwise.
    member _.IsEmpty : bool =
        xys.Count = 0


    /// Returns true if the path has three or more points
    /// (that is, six or more coordinates), false otherwise.
    /// A path needs at least three points to be a valid polygon, so this is a common check.
    /// But it might still have zero Area if the points are colinear.
    member _.HasThreeOrMorePoints : bool =
        xys.Count >= 6

    /// Gets the number of points in the path.
    /// This is half the length of the XYs ResizeArray, since X and Y are interleaved.
    member _.PointCount : int =
        Rarr.len xys / 2

    /// Gets the X ordinate at the given point index.
    /// Accesses the internal XYs ResizeArray via xys[index * 2].
    member _.GetX(index: int) : float =
        Rarr.getIdx (index * 2) xys

    /// Gets the Y ordinate at the given point index.
    /// Accesses the internal XYs ResizeArray via xys[index * 2 + 1].
    member _.GetY(index: int) : float =
        Rarr.getIdx (index * 2 + 1) xys

    /// Gets the Z value at the given point index.
    /// This is only valid if Z values were provided in the constructor, otherwise it throws an exception.
    member _.GetZ(index: int) : 'Z =
        match zs with
        | Some zs -> Rarr.getIdx index zs
        | None -> raise (InvalidOperationException "Path64.GetZ: This path does not have Z values.")

    /// Computes the total length of the path, considering it as an open path.
    /// Returns the sum of the distances between consecutive points.
    /// It does not include the distance from the last point back to the first point.
    member _.PathLength : float =
        let cnt = Rarr.len xys
        if cnt < 4 then
            0.0
        else
            let mutable total = 0.0
            let mutable prevX = Rarr.getIdx 0 xys
            let mutable prevY = Rarr.getIdx 1 xys
            let mutable i = 2
            while i <= cnt - 2 do
                let x = Rarr.getIdx i xys
                let y = Rarr.getIdx (i + 1) xys
                i <- i + 2
                let dx = x - prevX
                let dy = y - prevY
                total <- total + sqrt(dx * dx + dy * dy)
                prevX <- x
                prevY <- y
            total

    /// Computes the total length of the path, considering it as a closed path.
    /// Returns the sum of the distances between consecutive points,
    /// including the distance from the last point back to the first point.
    member _.ClosedPathLength : float =
        let cnt = xys.Count
        if cnt < 4 then
            0.0
        else
            let mutable total = 0.0
            let mutable prevX = Rarr.getIdx (cnt - 2) xys // start at last point's X
            let mutable prevY = Rarr.getIdx (cnt - 1) xys
            let mutable i = 0 // start at first point
            while i <= cnt - 2 do
                let x = Rarr.getIdx i xys
                let y = Rarr.getIdx (i + 1) xys
                i <- i + 2
                let dx = x - prevX
                let dy = y - prevY
                total <- total + sqrt(dx * dx + dy * dy)
                prevX <- x
                prevY <- y
            total

    /// Computes the area of the path using the shoelace formula.
    /// Positive for CCW in Cartesian / CW in screen coords.
    member p.SignedArea : float =
        // https://en.wikipedia.org/wiki/Shoelace_formula
        let cnt = p.PointCount
        if cnt < 3 then
            0.0
        else
            let coords = p.XYs
            let mutable total = 0.0
            let mutable prevCoord = (cnt - 1) * 2
            let mutable prevX = Rarr.getIdx prevCoord coords
            let mutable prevY = Rarr.getIdx (prevCoord + 1) coords
            for i = 0 to cnt - 1 do
                let coord = i * 2
                let x = Rarr.getIdx coord coords
                let y = Rarr.getIdx (coord + 1) coords
                total <- total + (prevY + y) * (prevX - x)
                prevX <- x
                prevY <- y
            total * 0.5

    /// Computes the area of the path.
    /// This is always a positive value,
    /// No matter the clockwise or counterclockwise direction of the path.
    member inline p.AbsArea : float =
        abs p.SignedArea



    /// Adds a new point and a Z value to the path.
    member internal _.Add(x: float, y: float, z: 'Z) : unit =
        xys.Add x
        xys.Add y
        match zs with
        | Some zs -> zs.Add z
        | None -> ()

    /// Clears all points and Z values from the path.
    member internal _.Clear() : unit =
        xys |> Rarr.clear
        match zs with
        | Some zs -> zs |> Rarr.clear
        | None -> ()


/// Contains a sequence of `Path64` structures, representing multiple contours.
/// Several paths make up a Clipper subject, e.g. an outer polygon with holes.
/// This is just a type alias for a ResizeArray of Path64.
type Paths64<'Z> =
    ResizeArray<Path64<'Z>>



type internal PointInPolygonResult =
    | IsOn = 0
    | IsInside = 1
    | IsOutside = 2

// #endregion
// #region module Geo

module internal Geo =

    /// For internal use only, always return 'Z even when it should be unit
    let inline emptyPath64<'Z> (hasZ:bool) : Path64<'Z> =
        if hasZ then
            Path64<'Z>(ResizeArray<float>(), Some (ResizeArray<'Z>()))
        else
            Path64<'Z>(ResizeArray<float>(), None)

    /// For internal use only, always return 'Z even when it should be unit
    let inline emptyPath64Sized<'Z> (hasZ:bool) (count:int) : Path64<'Z> =
        if hasZ then
            Path64<'Z>(ResizeArray<float>(count), Some (ResizeArray<'Z>(count)))
        else
            Path64<'Z>(ResizeArray<float>(count), None)


    /// abs (a - b) <= tol
    let inline isEqualWithin (tol: float) (a: float) (b: float) : bool =
        abs (a - b) <= tol

    /// abs (a - b) > tol
    let inline isNotEqualWithin (tol: float) (a: float) (b: float) : bool =
        abs (a - b) > tol


    /// Dimensionless tolerance for treating a cross product as zero, i.e. three points as
    /// colinear. Coordinates are no longer snapped to the integer grid, so an
    /// intersection point computed to lie on an edge is off by floating-point rounding
    /// error and the former exact test (a*b = c*d) almost never holds.
    ///
    /// The cross product of the two edge vectors U=(a,c) and W=(d,b) is
    /// `a*b - c*d = |U|*|W|*sin θ`, where θ is the turn angle at the shared point.
    /// Dividing by `|U|*|W|` therefore yields `sin θ`, so this constant is effectively
    /// an angle tolerance: points are colinear when the turn is within the configured tolerance. Using
    /// the edge-length scale (rather than the former `|a*b| + |c*d|`) keeps the test
    /// meaningful at any coordinate scale AND when both products are individually near
    /// zero — e.g. a near-horizontal or near-vertical spike, where a product-relative
    /// tolerance collapses to ~0 and the spike vertex is never recognized as colinear.
    /// This also lets colinear cleanup detect and close nearly 180-degree U-turn spikes.
    ///
    /// Stored pre-squared (this is `tolerance^2`) to avoid squaring in `crossIsZero`.
    /// Carried by the caller (e.g. `Clipper64.ColinearityTolerance`, which exposes the
    /// un-squared `sin θ` tolerance) rather than a module-global, so two clips can use
    /// different colinearity tolerances without interfering.

    /// True when the cross product of edge vectors U=(a,c) and W=(d,b) is effectively
    /// zero relative to the edge lengths, i.e. the three points are colinear, given the
    /// squared colinearity tolerance `colinTolSqrd`.
    /// Compared in squared form to avoid a sqrt: `(a*b - c*d)^2 <= tolerance^2 * |U|^2 * |W|^2`.
    let inline crossIsZero (colinTolSqrd: float) (a: float) (b: float) (c: float) (d: float) : bool =
        let cross = a * b - c * d
        let scaleSq = (a * a + c * c) * (b * b + d * d)
        cross * cross <= colinTolSqrd * scaleSq // needs `<=` because both sides might be zero


    let inline crossProductSign (colinTolSqrd: float) (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float, pt3X: float, pt3Y: float) : int =
        let a = pt2X - pt1X
        let b = pt3Y - pt2Y
        let c = pt2Y - pt1Y
        let d = pt3X - pt2X
        if crossIsZero colinTolSqrd a b c d then
            0
        elif a * b > c * d then
            1
        else
            -1


    let segsIntersectNotInclusive(colinTolSqrd: float, seg1aX: float, seg1aY: float, seg1bX: float, seg1bY: float, seg2aX: float, seg2aY: float, seg2bX: float, seg2bY: float) : bool =
        let s1 = crossProductSign colinTolSqrd (seg1aX, seg1aY, seg2aX, seg2aY, seg2bX, seg2bY)
        let s2 = crossProductSign colinTolSqrd (seg1bX, seg1bY, seg2aX, seg2aY, seg2bX, seg2bY)
        let s3 = crossProductSign colinTolSqrd (seg2aX, seg2aY, seg1aX, seg1aY, seg1bX, seg1bY)
        let s4 = crossProductSign colinTolSqrd (seg2bX, seg2bY, seg1aX, seg1aY, seg1bX, seg1bY)
        (s1 <> 0 && s2 <> 0 && s1 <> s2)
        &&
        (s3 <> 0 && s4 <> 0 && s3 <> s4)


    /// Returns true when the cross product a*b - c*d is effectively zero, i.e. the edge
    /// vectors U=(a,c) and W=(d,b) are colinear (to within the squared tolerance colinTolSqrd).
    /// (Formerly an exact comparison; relaxed now that coordinates carry floating-point
    /// error instead of lying on the integer grid.)
    let inline productsAreEqual (colinTolSqrd: float, a: float, b: float, c: float, d: float) : bool =
        crossIsZero colinTolSqrd a b c d

    let isColinear (colinTolSqrd: float, pt1X: float, pt1Y: float, sharedX: float, sharedY: float, pt2X: float, pt2Y: float) : bool =
        let a = sharedX - pt1X
        let b = pt2Y - sharedY
        let c = sharedY - pt1Y
        let d = pt2X - sharedX
        productsAreEqual (colinTolSqrd, a, b, c, d)

    let inline dotProduct (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float, pt3X: float, pt3Y: float) : float =
        let a = pt2X - pt1X
        let b = pt3X - pt2X
        let c = pt2Y - pt1Y
        let d = pt3Y - pt2Y
        a * b + c * d

    let inline dotProductSign (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float, pt3X: float, pt3Y: float) : int =
        let sum = dotProduct (pt1X, pt1Y, pt2X, pt2Y, pt3X, pt3Y)
        if sum > 0.0 then
            1
        elif sum < 0.0 then
            -1
        else
            0

    let pointInPolygon (coordEqTol: float, colinTolSqrd: float, ptX: float, ptY: float, polygon: Path64<'Z>) : PointInPolygonResult =
        let inline isEqual a b = isEqualWithin coordEqTol a b
        let inline crossProductSign args = crossProductSign colinTolSqrd args
        let len = polygon.PointCount
        if len < 3 then
            PointInPolygonResult.IsOutside
        else
            let coords = polygon.XYs
            let inline getX i = Rarr.getIdx (i * 2) coords
            let inline getY i = Rarr.getIdx (i * 2 + 1) coords
            let mutable start = 0
            while start < len && isEqual (getY start) ptY do
                start <- start + 1
            if start = len then
                PointInPolygonResult.IsOutside
            else
                let mutable isAbove = getY start < ptY
                let startingAbove = isAbove
                let mutable valToggle = 0
                let mutable i = start + 1
                let mutable endIdx = len
                let mutable loopOn = true
                let mutable hasResult = false
                let mutable result = PointInPolygonResult.IsOutside

                while loopOn do
                    let mutable skip = false

                    if i = endIdx then
                        if endIdx = 0 || start = 0 then
                            loopOn <- false
                            skip <- true
                        else
                            endIdx <- start
                            i <- 0

                    if loopOn && not skip then
                        if isAbove then
                            while i < endIdx && getY i < ptY do i <- i + 1
                        else
                            while i < endIdx && getY i > ptY do i <- i + 1

                        if i = endIdx then
                            skip <- true  // continue — wrap around
                        else
                            let currX = getX i
                            let currY = getY i
                            let prevIdx = if i > 0 then i - 1 else len - 1
                            let prevX = getX prevIdx
                            let prevY = getY prevIdx

                            if isEqual currY ptY then
                                if isEqual currX ptX ||
                                   (isEqual currY prevY && ((ptX < prevX) <> (ptX < currX))) then
                                    hasResult <- true
                                    result <- PointInPolygonResult.IsOn
                                    loopOn <- false
                                else
                                    i <- i + 1
                                    if i = start then
                                        loopOn <- false
                                skip <- true

                            if loopOn && not skip then
                                if ptX < currX && ptX < prevX then
                                    ()  // edge entirely to the right — ignore
                                elif ptX > prevX && ptX > currX then
                                    valToggle <- 1 - valToggle
                                else
                                    let cps = crossProductSign (prevX, prevY, currX, currY, ptX, ptY)
                                    if cps = 0 then
                                        hasResult <- true
                                        result <- PointInPolygonResult.IsOn
                                        loopOn <- false
                                    elif (cps < 0) = isAbove then
                                        valToggle <- 1 - valToggle

                                if loopOn then
                                    isAbove <- not isAbove
                                    i <- i + 1

                if hasResult then
                    result
                elif isAbove = startingAbove then
                    if valToggle = 0 then
                        PointInPolygonResult.IsOutside
                    else
                        PointInPolygonResult.IsInside
                else
                    if i = len then
                        i <- 0
                    let cps =
                        if i = 0 then
                            crossProductSign (getX (len - 1), getY (len - 1), getX 0, getY 0, ptX, ptY)
                        else
                            crossProductSign (getX (i - 1), getY (i - 1), getX i, getY i, ptX, ptY)
                    if cps = 0 then
                        PointInPolygonResult.IsOn
                    else
                        if (cps < 0) = isAbove then
                            valToggle <- 1 - valToggle
                        if valToggle = 0 then
                            PointInPolygonResult.IsOutside
                        else
                            PointInPolygonResult.IsInside

    let path2ContainsPath1 (coordEqTol: float) (colinTolSqrd: float) (path1: Path64<'Z>) (path2: Path64<'Z>) : bool =
        // We need to make some accommodation for rounding errors so we don't
        // jump if the first vertex is found outside.
        let mutable pip = PointInPolygonResult.IsOn
        let mutable earlyDone = false
        let mutable earlyResult = false
        let mutable i = 0
        let coords = path1.XYs
        while not earlyDone && i < path1.PointCount do
            let coord = i * 2
            match pointInPolygon (coordEqTol, colinTolSqrd, Rarr.getIdx coord coords, Rarr.getIdx (coord + 1) coords, path2) with
            | PointInPolygonResult.IsOutside ->
                if pip = PointInPolygonResult.IsOutside then
                    earlyResult <- false
                    earlyDone <- true
                else
                    pip <- PointInPolygonResult.IsOutside
            | PointInPolygonResult.IsInside ->
                if pip = PointInPolygonResult.IsInside then
                    earlyResult <- true
                    earlyDone <- true
                else
                    pip <- PointInPolygonResult.IsInside
            | _ -> ()
            i <- i + 1

        if earlyDone then
            earlyResult
        else
            // since path1's location is still equivocal, check its midpoint
            // let getBounds (path: Path64) : Rect64 = // inlined here:
            if path1.PointCount = 0 then // can this happen here?
                false
            else
                let coords = path1.XYs
                let mutable left = Double.MaxValue
                let mutable top = Double.MaxValue
                let mutable right = Double.MinValue
                let mutable bottom = Double.MinValue
                for i = 0 to path1.PointCount - 1 do
                    let coord = i * 2
                    let x = Rarr.getIdx coord coords
                    let y = Rarr.getIdx (coord + 1) coords
                    if x < left   then left <- x
                    if x > right  then right <- x
                    if y < top    then top <- y
                    if y > bottom then bottom <- y
                let midX = (left + right) * 0.5 // no more rounding (to int64) here
                let midY = (top + bottom) * 0.5 // no more rounding (to int64) here
                pointInPolygon (coordEqTol, colinTolSqrd, midX, midY, path2) <> PointInPolygonResult.IsOutside

    /// Reverses a path (returns a new Path64).
    let reversePath (path: Path64<'Z>) : Path64<'Z> =
        let cnt = path.PointCount
        let hasZs = path.HasZs
        let result = emptyPath64Sized<'Z> hasZs cnt
        let xys = path.XYs
        let resXYs = result.XYs
        if hasZs then
            let pathZs = path.Zs.Value
            let resZs = result.Zs.Value
            for i = cnt - 1 downto 0 do
                resXYs.Add(Rarr.getIdx (i * 2) xys)
                resXYs.Add(Rarr.getIdx (i * 2 + 1) xys)
                resZs.Add(Rarr.getIdx i pathZs)
        else
            for i = cnt - 1 downto 0 do
                resXYs.Add(Rarr.getIdx (i * 2) xys)
                resXYs.Add(Rarr.getIdx (i * 2 + 1) xys)
        result





