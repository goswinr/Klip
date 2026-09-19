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

#if !FABLE_COMPILER
// Exercise geometry predicates directly without exposing them as public API.
[<assembly: Runtime.CompilerServices.InternalsVisibleTo("Tests1")>]
do ()
#endif

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
            raise (ArgumentException $"Path64 constructor: xys.Count ({xys.Count}) must be even")
        match zs with
        |Some zs ->
            let pointCount = xys.Count / 2
            if zs.Count <> pointCount then
                raise (ArgumentException $"Path64 constructor: zs.Count ({zs.Count}) <> point count ({pointCount}) in xys.")
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
            // Translate to a local origin before multiplying. Absolute-coordinate
            // shoelace terms can erase a small area far from the global origin.
            let ox, oy = p.GetX 0, p.GetY 0
            let mutable total = 0.
            let mutable correction = 0.
            for i = 1 to cnt - 2 do
                let ax, ay = p.GetX i - ox, p.GetY i - oy
                let bx, by = p.GetX (i+1) - ox, p.GetY (i+1) - oy
                let term = (ax * by - ay * bx) - correction
                let sum = total + term
                correction <- (sum - total) - term
                total <- sum
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
    /// zero - e.g. a near-horizontal or near-vertical spike, where a product-relative
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
    /// Normalize each vector separately so neither fourth powers nor tiny cross-product
    /// squares can overflow or underflow. The stored tolerance is still pre-squared.
    let inline crossIsZero (colinTolSqrd: float) (a: float) (b: float) (c: float) (d: float) : bool =
        let uScale = max (abs a) (abs c)
        let vScale = max (abs b) (abs d)
        if uScale = 0. || vScale = 0. then true
        else
            let ax, cy = a / uScale, c / uScale
            let by, dx = b / vScale, d / vScale
            abs (ax * by - cy * dx) <= sqrt colinTolSqrd * sqrt ((ax*ax + cy*cy) * (by*by + dx*dx))


    /// Orientation for topology, independent of the angle used for colinear cleanup.
    /// A shallow but nonzero turn still determines which side of an edge a point lies on.
    let inline crossProductSign (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float, pt3X: float, pt3Y: float) : int =
        let left = (pt2X - pt1X) * (pt3Y - pt1Y)
        let right = (pt2Y - pt1Y) * (pt3X - pt1X)
        if left > right then 1
        elif left < right then -1
        else 0

    let segsIntersectNotInclusive(seg1aX: float, seg1aY: float, seg1bX: float, seg1bY: float, seg2aX: float, seg2aY: float, seg2bX: float, seg2bY: float) : bool =
        let s1 = crossProductSign (seg2aX, seg2aY, seg2bX, seg2bY, seg1aX, seg1aY)
        let s2 = crossProductSign (seg2aX, seg2aY, seg2bX, seg2bY, seg1bX, seg1bY)
        let s3 = crossProductSign (seg1aX, seg1aY, seg1bX, seg1bY, seg2aX, seg2aY)
        let s4 = crossProductSign (seg1aX, seg1aY, seg1bX, seg1bY, seg2bX, seg2bY)
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

    let dotProductSign (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float, pt3X: float, pt3Y: float) : int =
        let ax, ay = pt2X - pt1X, pt2Y - pt1Y
        let bx, by = pt3X - pt2X, pt3Y - pt2Y
        let aScale, bScale = max (abs ax) (abs ay), max (abs bx) (abs by)
        let sum =
            if aScale = 0. || bScale = 0. then 0.
            else (ax / aScale) * (bx / bScale) + (ay / aScale) * (by / bScale)
        // 0.0 is OK to check against, no tolerance needed here ,
        // Its only caller first checks collinearity and removes coincident vertices ([Engine.fs (line 2019)](/D:/Git/_Euclid_/Klip/Src/Engine.fs:2019)).
        // It then distinguishes a straight continuation from a U-turn: the normalized dot product is near +1 or −1, safely away from zero.
        // A fixed epsilon would also introduce a scale-dependent threshold in squared coordinate units.
        if sum > 0.0 then
            1
        elif sum < 0.0 then
            -1
        else
            0

    /// Absolute distance to the infinite line, without squaring coordinate magnitudes.
    /// Coincident line endpoints describe a zero-width spike for cleanup purposes.
    let pointWithinLineDistance (tolerance: float) (ptX: float, ptY: float, ax: float, ay: float, bx: float, by: float) : bool =
        if crossProductSign (ax, ay, bx, by, ptX, ptY) = 0 then true
        elif tolerance = 0.0 then false
        else
            let dx = bx - ax
            let dy = by - ay
            let scale = max (abs dx) (abs dy)
            let ux = dx / scale
            let uy = dy / scale
            abs ((ptX - ax) * uy - (ptY - ay) * ux) <= tolerance * sqrt (ux * ux + uy * uy)

    /// Boundary proximity is an absolute distance, never an angle from an endpoint.
    /// Bound the segment in both axes (including endpoint coincidence), then check
    /// perpendicular distance using a scaled direction to avoid squaring edge lengths.
    let inline pointOnSegment (coordEqTol: float) (ptX: float, ptY: float, ax: float, ay: float, bx: float, by: float) : bool =
        if ptX < min ax bx - coordEqTol || ptX > max ax bx + coordEqTol ||
           ptY < min ay by - coordEqTol || ptY > max ay by + coordEqTol then
            false
        elif (isEqualWithin coordEqTol ptX ax && isEqualWithin coordEqTol ptY ay) ||
             (isEqualWithin coordEqTol ptX bx && isEqualWithin coordEqTol ptY by) then
            true
        elif crossProductSign (ax, ay, bx, by, ptX, ptY) = 0 then
            // Recognize exact incidence before normalizing the direction: rounding
            // that direction must not dislodge a boundary point at tiny tolerances.
            true
        elif coordEqTol = 0.0 then false
        else
            let dx = bx - ax
            let dy = by - ay
            let scale = max (abs dx) (abs dy)
            if scale = 0.0 then false
            else
                let ux = dx / scale
                let uy = dy / scale
                abs ((ptX - ax) * uy - (ptY - ay) * ux) <= coordEqTol * sqrt (ux * ux + uy * uy)

    let pointInPolygon (coordEqTol: float, ptX: float, ptY: float, polygon: Path64<'Z>) : PointInPolygonResult =
        let len = polygon.PointCount
        if len < 3 then
            PointInPolygonResult.IsOutside
        else
            let mutable ax = polygon.GetX (len - 1)
            let mutable ay = polygon.GetY (len - 1)
            let mutable inside = false
            let mutable onBoundary = false
            let mutable i = 0
            while i < len && not onBoundary do
                let bx = polygon.GetX i
                let by = polygon.GetY i
                if pointOnSegment coordEqTol (ptX, ptY, ax, ay, bx, by) then
                    onBoundary <- true
                // Half-open ray crossings must use exact Y ordering so a vertex
                // is counted only once. Distance tolerance belongs only above.
                elif (ay > ptY) <> (by > ptY) then
                    let side = crossProductSign (ax, ay, bx, by, ptX, ptY)
                    if (side > 0) = (by > ay) then inside <- not inside
                ax <- bx
                ay <- by
                i <- i + 1
            if onBoundary then PointInPolygonResult.IsOn
            elif inside then PointInPolygonResult.IsInside
            else PointInPolygonResult.IsOutside

    let path2ContainsPath1 (coordEqTol: float) (path1: Path64<'Z>) (path2: Path64<'Z>) : bool =
        // We need to make some accommodation for rounding errors so we don't
        // jump if the first vertex is found outside.
        let mutable pip = PointInPolygonResult.IsOn
        let mutable earlyDone = false
        let mutable earlyResult = false
        let mutable i = 0
        let coords = path1.XYs
        while not earlyDone && i < path1.PointCount do
            let coord = i * 2
            match pointInPolygon (coordEqTol, Rarr.getIdx coord coords, Rarr.getIdx (coord + 1) coords, path2) with
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
                pointInPolygon (coordEqTol, midX, midY, path2) <> PointInPolygonResult.IsOutside

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





