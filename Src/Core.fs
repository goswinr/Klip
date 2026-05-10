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

open System

type OPT = Runtime.InteropServices.OptionalAttribute
type DEF = Runtime.InteropServices.DefaultParameterValueAttribute



[<AutoOpen>]
module internal Operators =

    let inline ( === ) (x: obj) (y: obj) : bool =
        Object.ReferenceEquals(x, y)

    let inline ( =!= ) (x: obj) (y: obj) : bool =
        not (Object.ReferenceEquals(x, y))

//#region Null module

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

//#region Rarr module

[<RequireQualifiedAccess>]
module internal Rarr =

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
        for i = 0 to resizeArray.Count - 1 do
            mapping resizeArray[i]




//#endregion
//#region type Path64


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
/// When a Path64 is created without Z values, it's type will be Path64<unit>, and the Zs member will be None.
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


    // /// Scales all points in the path by the given factor.
    // /// This is a mutating operation that modifies the path in place.
    // member _.Scale(factor: float) : unit =
    //     let coords = xys
    //     for i = 0 to coords.Count - 1 do
    //         coords[i] <- jsRound(coords[i] * factor)


    /// Gets the number of points in the path.
    /// This is half the length of the XYs ResizeArray, since X and Y are interleaved.
    member _.PointCount : int =
        xys.Count / 2


    /// Gets the X ordinate at the given point index.
    /// Accesses the internal XYs ResizeArray via xys[index * 2].
    member _.GetX(index: int) : float =
        xys.[index * 2]

    /// Gets the Y ordinate at the given point index.
    /// Accesses the internal XYs ResizeArray via xys[index * 2 + 1].
    member _.GetY(index: int) : float =
        xys.[index * 2 + 1]

    /// Gets the Z value at the given point index.
    /// This is only valid if Z values were provided in the constructor, otherwise it throws an exception.
    member _.GetZ(index: int) : 'Z =
        match zs with
        | Some zs -> zs.[index]
        | None -> raise (InvalidOperationException "Path64.GetZ: This path does not have Z values.")

    /// Computes the total length of the path,
    /// which is the sum of the distances between consecutive points.
    member _.PathLength : float =
        let cnt = xys.Count
        if cnt < 4 then
            0.0
        else
            let mutable total = 0.0
            let mutable prevX = xys[0]
            let mutable prevY = xys[1]
            let mutable i = 2
            while i <= xys.Count - 2 do
                let x = xys[i]
                let y = xys[i + 1]
                i <- i + 2
                let dx = x - prevX
                let dy = y - prevY
                total <- total + sqrt(dx * dx + dy * dy)
                prevX <- x
                prevY <- y
            total

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

//#endregion
//#region module Geo

module internal Geo =

    /// For internal use only, always return 'Z even when it should be unit
    let inline emptyPath64<'Z> (hasZ:bool) : Path64<'Z> =
        if hasZ then
            Path64<'Z>(ResizeArray<float>(), Some (ResizeArray<'Z>()))
        else
            Path64<'Z>(ResizeArray<float>(), None)


    /// Rounds a float to the nearest integer, using
    /// `Math.round` in Fable and `Math.Round` in .NET.
    ///  While JS does Round-half-towards-positive-infinity.
    /// .NET does Rounds-half-to-even, (Banker's Rounding).
    let inline jsRound (x: float) : float =
        #if FABLE_COMPILER_JAVASCRIPT || FABLE_COMPILER_TYPESCRIPT
            Fable.Core.JsInterop.emitJsExpr x "Math.round($0)"
        #else
            Math.Round x
        #endif


    let inline crossProductSign (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float, pt3X: float, pt3Y: float) : int =
        let a = pt2X - pt1X
        let b = pt3Y - pt2Y
        let c = pt2Y - pt1Y
        let d = pt3X - pt2X
        // Fast check for safe integer range
        // Using Math.Abs inline allows short-circuiting
        let prod1 = a * b
        let prod2 = c * d
        if prod1 > prod2 then
            1
        elif prod1 < prod2 then
            -1
        else
            0


    let segsIntersectNotInclusive(seg1aX: float, seg1aY: float, seg1bX: float, seg1bY: float, seg2aX: float, seg2aY: float, seg2bX: float, seg2bY: float) : bool =
        let s1 = crossProductSign (seg1aX, seg1aY, seg2aX, seg2aY, seg2bX, seg2bY)
        let s2 = crossProductSign (seg1bX, seg1bY, seg2aX, seg2aY, seg2bX, seg2bY)
        let s3 = crossProductSign (seg2aX, seg2aY, seg1aX, seg1aY, seg1bX, seg1bY)
        let s4 = crossProductSign (seg2bX, seg2bY, seg1aX, seg1aY, seg1bX, seg1bY)
        (s1 <> 0 && s2 <> 0 && s1 <> s2)
        &&
        (s3 <> 0 && s4 <> 0 && s3 <> s4)


    // Returns true if (and only if) a*b == c*d.
    // When checking colinearity with very large coordinate values this is more
    // accurate than using crossProduct (see TS Core.ts).
    let inline productsAreEqual (a: float, b: float, c: float, d: float) : bool =
        a * b = c * d

    let isCollinear (pt1X: float, pt1Y: float, sharedX: float, sharedY: float, pt2X: float, pt2Y: float) : bool =
        let a = sharedX - pt1X
        let b = pt2Y - sharedY
        let c = sharedY - pt1Y
        let d = pt2X - sharedX
        productsAreEqual (a, b, c, d)

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

    // https://en.wikipedia.org/wiki/Shoelace_formula
    let area (path: Path64<'Z>) : float =
        let cnt = path.PointCount
        if cnt < 3 then 0.0
        else
            let coords = path.XYs
            let mutable total = 0.0
            let mutable prevCoord = (cnt - 1) * 2
            let mutable prevX = coords[prevCoord]
            let mutable prevY = coords[prevCoord + 1]
            for i = 0 to cnt - 1 do
                let coord = i * 2
                let x = coords[coord]
                let y = coords[coord + 1]
                total <- total + (prevY + y) * (prevX - x)
                prevX <- x
                prevY <- y
            total * 0.5




    let pointInPolygon (ptX: float, ptY: float, polygon: Path64<'Z>) : PointInPolygonResult =
        let len = polygon.PointCount
        if len < 3 then
            PointInPolygonResult.IsOutside
        else
            let coords = polygon.XYs
            let inline getX i = coords[i * 2]
            let inline getY i = coords[i * 2 + 1]
            let mutable start = 0
            while start < len && getY start = ptY do
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

                            if currY = ptY then
                                if currX = ptX ||
                                   (currY = prevY && ((ptX < prevX) <> (ptX < currX))) then
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

    let path2ContainsPath1 (path1: Path64<'Z>) (path2: Path64<'Z>) : bool =
        // We need to make some accommodation for rounding errors so we don't
        // jump if the first vertex is found outside.
        let mutable pip = PointInPolygonResult.IsOn
        let mutable earlyDone = false
        let mutable earlyResult = false
        let mutable i = 0
        let coords = path1.XYs
        while not earlyDone && i < path1.PointCount do
            let coord = i * 2
            match pointInPolygon (coords[coord], coords[coord + 1], path2) with
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
                    let x = coords[coord]
                    let y = coords[coord + 1]
                    if x < left   then left <- x
                    if x > right  then right <- x
                    if y < top    then top <- y
                    if y > bottom then bottom <- y
                let midX = jsRound((left + right) * 0.5)
                let midY = jsRound((top + bottom) * 0.5)
                pointInPolygon (midX, midY, path2) <> PointInPolygonResult.IsOutside





