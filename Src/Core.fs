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

    let inline jsRound (x: float) : float =
        #if FABLE_COMPILER
            Fable.Core.JsInterop.emitJsExpr x "Math.round($0)"
        #else
            Math.Round x
        #endif


module internal Null =

    let inline isNull' (x: obj) : bool =
        #if FABLE_COMPILER
            Fable.Core.JsInterop.emitJsExpr x "$0 === null"
        #else
            x === null
        #endif


    let inline isNotNull (x: obj) : bool =
        #if FABLE_COMPILER
            Fable.Core.JsInterop.emitJsExpr x "$0 !== null"
        #else
            x =!= null
        #endif

    /// Needed for cheating the F# compiler to set F# records to null.
    let inline null'() : 'T =
        Unchecked.defaultof<'T> // needed for Fable TS build
        // #if FABLE_COMPILER
        //     Fable.Core.JsInterop.emitJsExpr () "null" // to avoid emitting defaultOf() call, but  this seems irrelevant to performance.
        // #else
        //     Unchecked.defaultof<'T>
        // #endif


    let [<Literal>] DEFZ =
        null


[<RequireQualifiedAccess>]
module internal Rarr =

    /// this is more efficient than ResizeArray.Clear() in Fable,
    /// which emits .splice(0)
    let inline clear (arr: ResizeArray<'T>) : unit =
        #if FABLE_COMPILER
            Fable.Core.JsInterop.emitJsStatement arr "$0.length = 0"
        #else
            arr.Clear()
        #endif

    let inline pop (arr: ResizeArray<'T>) : unit =
        #if FABLE_COMPILER
            Fable.Core.JsInterop.emitJsStatement arr "$0.pop()"
        #else
            arr.RemoveAt(arr.Count - 1)
        #endif



/// Contains a sequence of vertices defining a single contour.
/// The path stores X, Y ordinates in three parallel float buffers.
[<AllowNullLiteral>]
type Path64( xs:ResizeArray<float>, ys:ResizeArray<float>, zs:ResizeArray<obj>) =


    new() =
        Path64(
            ResizeArray<float>(),
            ResizeArray<float>(),
            ResizeArray<obj>())


    /// Gets the X ordinates of the path.
    member _.Xs : ResizeArray<float> = xs
    /// Gets the Y ordinates of the path.
    member _.Ys : ResizeArray<float> = ys
    /// Gets the Z coordinates of the path.
    member _.Zs : ResizeArray<obj> = zs

    /// Gets the number of vertices in the path.
    member _.Count : int =
        xs.Count

    /// Adds a new vertex to the path.
    member this.Add(x: float, y: float, z: obj) : unit =
        xs.Add x
        ys.Add y
        zs.Add z

    /// Clears all vertices from the path.
    member _.Clear() : unit =
        xs |> Rarr.clear
        ys |> Rarr.clear
        zs |> Rarr.clear



/// Contains a sequence of `Path64` structures, representing multiple contours.
/// Several paths make up a Clipper subject, e.g. an outer polygon with holes.
type Paths64 =
    ResizeArray<Path64>


type internal PointInPolygonResult =
    | IsOn = 0
    | IsInside = 1
    | IsOutside = 2


//#region module Geo

module internal Geo =

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
    let area (path: Path64) : float =
        let cnt = path.Count
        if cnt < 3 then 0.0
        else
            let xs = path.Xs
            let ys = path.Ys
            let mutable total = 0.0
            let mutable prevX = xs[cnt - 1]
            let mutable prevY = ys[cnt - 1]
            for i = 0 to cnt - 1 do
                let x = xs[i]
                let y = ys[i]
                total <- total + (prevY + y) * (prevX - x)
                prevX <- x
                prevY <- y
            total * 0.5




    let pointInPolygon (ptX: float, ptY: float, polygon: Path64) : PointInPolygonResult =
        let len = polygon.Count
        if len < 3 then
            PointInPolygonResult.IsOutside
        else
            let mutable start = 0
            while start < len && polygon.Ys[start] = ptY do
                start <- start + 1
            if start = len then
                PointInPolygonResult.IsOutside
            else
                let mutable isAbove = polygon.Ys[start] < ptY
                let startingAbove = isAbove
                let mutable valToggle = 0
                let mutable i = start + 1
                let mutable endIdx = len
                let mutable loopOn = true
                let mutable hasResult = false
                let mutable result = PointInPolygonResult.IsOutside

                let xs = polygon.Xs
                let ys = polygon.Ys

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
                            while i < endIdx && polygon.Ys[i] < ptY do i <- i + 1
                        else
                            while i < endIdx && polygon.Ys[i] > ptY do i <- i + 1

                        if i = endIdx then
                            skip <- true  // continue — wrap around
                        else
                            let currX = xs[i]
                            let currY = ys[i]
                            let prevIdx = if i > 0 then i - 1 else len - 1
                            let prevX = xs[prevIdx]
                            let prevY = ys[prevIdx]

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
                            crossProductSign (xs[len - 1], ys[len - 1], xs[0], ys[0], ptX, ptY)
                        else
                            crossProductSign (xs[i - 1], ys[i - 1], xs[i], ys[i], ptX, ptY)
                    if cps = 0 then
                        PointInPolygonResult.IsOn
                    else
                        if (cps < 0) = isAbove then
                            valToggle <- 1 - valToggle
                        if valToggle = 0 then
                            PointInPolygonResult.IsOutside
                        else
                            PointInPolygonResult.IsInside

    let path2ContainsPath1 (path1: Path64) (path2: Path64) : bool =
        // We need to make some accommodation for rounding errors so we don't
        // jump if the first vertex is found outside.
        let mutable pip = PointInPolygonResult.IsOn
        let mutable earlyDone = false
        let mutable earlyResult = false
        let mutable i = 0
        let xs = path1.Xs
        let ys = path1.Ys
        while not earlyDone && i < path1.Count do
            match pointInPolygon (xs[i], ys[i], path2) with
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
            if path1.Count = 0 then // can this happen here?
                false
            else
                let xs = path1.Xs
                let ys = path1.Ys
                let mutable left = Double.MaxValue
                let mutable top = Double.MaxValue
                let mutable right = Double.MinValue
                let mutable bottom = Double.MinValue
                for i = 0 to path1.Count - 1 do
                    let x = xs[i]
                    let y = ys[i]
                    if x < left   then left <- x
                    if x > right  then right <- x
                    if y < top    then top <- y
                    if y > bottom then bottom <- y
                let midX = jsRound((left + right) * 0.5)
                let midY = jsRound((top + bottom) * 0.5)
                pointInPolygon (midX, midY, path2) <> PointInPolygonResult.IsOutside





