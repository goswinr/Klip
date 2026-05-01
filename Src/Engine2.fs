(* ******************************************************************************
* Author    :  Angus Johnson                                                   *
* Date      :  21 February 2026                                                *
* Website   :  https://www.angusj.com                                          *
* Copyright :  Angus Johnson 2010-2025                                         *
* Purpose   :  This is the main polygon clipping module                        *
* Thanks    :  Special thanks to Thong Nguyen, Guus Kuiper, Phil Stopford,     *
*           :  and Daniel Gosnell for their invaluable assistance with C#.     *
* License   :  https://www.boost.org/LICENSE_1_0.txt                           *
****************************************************************************** *)

// ported to TypeScript at https://github.com/countertype/clipper2-ts
// then ported to F# and simplified here:

namespace Klip

open System
open System.Collections.Generic
open Klip.Null
open Klip.LipInternal

/// A delegate or function pointer used to set Z-coordinates on intersection points
/// when using the optional "Z" coordinate feature of Clipper2.
///
/// While most vertices in clipping solutions will correspond to input (subject and clip) vertices,
/// there will also be new vertices wherever these segments intersect.
/// This callback facilitates assigning user-defined Z values at these intersections.
/// To aid the user in determining appropriate Z values,
/// the function receives the vertices at both ends of the intersecting segments (ie four vertices).
///
/// Receives the adjacent active edges and the computed intersection ordinates, and returns the new Z value.
/// NULL warning: maybe ActiveEdge may be null when the callback is invoked from doSplitOp.
type ZCallback64 =
    ActiveEdge * ActiveEdge * float * float * obj -> float


type [<NoComparison;NoEquality>] internal State = {
    /// `openPathsEnabled` is a process-global flag set by Clipper64 before each execute.
    mutable openPathsEnabled: bool
    mutable tempX: float
    mutable tempY: float
    mutable tempZ: obj
}

//#region module Clip

// Internal helper functions that were `private static` on ClipperBase in TS.
module internal Clip =

    let state: State =
        {
        openPathsEnabled = true
        tempX = 0.0
        tempY = 0.0
        tempZ = Null.DEFZ
        }

    // same as getLineIntersectPt but only called from doSplitOp
    let getLineIntersectPtInState(ln1a: OutPt, ln1b: OutPt, ln2a: OutPt, ln2b: OutPt) : unit =
        let dy1 = ln1b.y - ln1a.y
        let dx1 = ln1b.x - ln1a.x
        let dy2 = ln2b.y - ln2a.y
        let dx2 = ln2b.x - ln2a.x
        let det = dy1 * dx2 - dy2 * dx1
        // doSplitOp is only reached when segments are known to intersect,
        // so det is never zero here and can be used as divisor. (see clipper2-ts comments)
        let t = ((ln1a.x - ln2a.x) * dy2 - (ln1a.y - ln2a.y) * dx2) / det
        if t <= 0.0 then
            state.tempX <- ln1a.x
            state.tempY <- ln1a.y
            state.tempZ <- ln1a.z
        elif t >= 1.0 then
            state.tempX <- ln1b.x
            state.tempY <- ln1b.y
            state.tempZ <- ln1b.z
        else
            // C#: avoid using constructor (and rounding too) as they affect performance //664
            state.tempX <- Math.Truncate(ln1a.x + t * dx1)
            state.tempY <- Math.Truncate(ln1a.y + t * dy1)
            state.tempZ <- Null.DEFZ


    // GetLineIntersectPt - returns the intersection point if non-parallel, or null.
    // The point will be constrained to seg1. However, it's possible that the point
    // won't be inside seg2, even when it hasn't been constrained (ie inside seg1).
    let getLineIntersectPt(ae1: ActiveEdge, ae2: ActiveEdge, topY: float) : IntersectNode =
        // TS comment:
        // GetLineIntersectPt - returns the intersection point if non-parallel, or null.
        // The point will be constrained to seg1. However, it's possible that the point
        // won't be inside seg2, even when it hasn't been constrained (ie inside seg1).
        // Returns Point64 | null to avoid allocating a wrapper object on every call.
        // getLineIntersectPt(ae1.bot, ae1.top, ae2.bot, ae2.top);
        // getLineIntersectPt(  ln1a: Point64, ln1b: Point64,   ln2a: Point64, ln2b: Point64)

        // C# comment:
        // GetLineIntersectPt - a 'true' result is non-parallel. The 'ip' will also
        // be constrained to seg1. However, it's possible that 'ip' won't be inside
        // seg2, even when 'ip' hasn't been constrained (ie 'ip' is inside seg1).

        let dy1 = ae1.topY - ae1.botY
        let dx1 = ae1.topX - ae1.botX
        let dy2 = ae2.topY - ae2.botY
        let dx2 = ae2.topX - ae2.botX
        let det = dy1 * dx2 - dy2 * dx1
        if det = 0.0 then
            { // see AddNewIntersectNode:
            x = ae1.curX
            y = topY
            z = Null.DEFZ
            edge1 = ae1
            edge2 = ae2
            }
        else
            let t = ((ae1.botX - ae2.botX) * dy2 - (ae1.botY - ae2.botY) * dx2) / det
            if t <= 0.0 then
                {
                x = ae1.botX
                y = ae1.botY
                z = ae1.botZ
                edge1 = ae1
                edge2 = ae2
                }
            elif t >= 1.0 then
                {
                x = ae1.topX
                y = ae1.topY
                z = ae1.topZ
                edge1 = ae1
                edge2 = ae2
                }
            else
                // C#: avoid using constructor (and rounding too) as they affect performance //664
                // Use Math.trunc to match C# (long) cast behavior which truncates towards zero
                {
                x = Math.Truncate(ae1.botX + t * dx1)
                y = Math.Truncate(ae1.botY + t * dy1)
                z = Null.DEFZ
                edge1 = ae1
                edge2 = ae2
                }

    // actually should be called setClosestPtOnSegment, but kept the original name for consistency
    let getClosestPtOnSegment (xn: IntersectNode, seg1X: float, seg1Y: float, seg2X: float, seg2Y: float): unit =
        if seg1X = seg2X && seg1Y = seg2Y then
            xn.x <- seg1X
            xn.y <- seg1Y
        else
            let dx = seg2X - seg1X
            let dy = seg2Y - seg1Y
            let q = ((xn.x - seg1X) * dx + (xn.y - seg1Y) * dy) / (dx * dx + dy * dy)
            let qClamped =
                if q < 0.0 then
                    0.0
                elif q > 1.0 then
                    1.0
                else
                    q
            // use MidpointRounding.ToEven in order to explicitly match the nearbyint behaviour on the C++ side
            xn.x <- jsRound(seg1X + qClamped * dx) //, MidpointRounding.ToEven)
            xn.y <- jsRound(seg1Y + qClamped * dy) //, MidpointRounding.ToEven)

    let inline localMinimaEqual (localMinima: LocalMinima, other: LocalMinima) : bool =
        isNotNull other
        &&
        localMinima.vertex === other.vertex

    let inline xyEqual(ax: float, ay: float, bx: float, by: float) : bool =
        ax = bx && ay = by

    let inline xyNotEqual(ax: float, ay: float, bx: float, by: float) : bool =
        ax <> bx || ay <> by

    let inline isOdd (v: int) : bool =
        (v &&& 1) <> 0

    let inline isHotEdge (ae: ActiveEdge) : bool =
        isNotNull ae.outrec

    let inline isOpen (ae: ActiveEdge) : bool =
        state.openPathsEnabled && ae.localMin.isOpen

    let inline isOpenEndVertex (v: Vertex) : bool =
        v.flags &&& (VertexFlags.OpenStart ||| VertexFlags.OpenEnd) <> VertexFlags.None

    let inline isOpenEnd (ae: ActiveEdge) : bool =
        state.openPathsEnabled &&
        ae.localMin.isOpen &&
        isOpenEndVertex(ae.vertexTop)

    let getPrevHotEdge (ae: ActiveEdge) : ActiveEdge =
        let mutable prev = ae.prevInAEL
        if not state.openPathsEnabled then
            // Fast path: when open paths are disabled, avoid calling isOpen() in the loop.
            while isNotNull prev && not (isHotEdge prev) do
                prev <- prev.prevInAEL
            prev
        else
            while isNotNull prev && (prev.localMin.isOpen || not (isHotEdge prev)) do
                prev <- prev.prevInAEL
            prev

    let inline isFront (ae: ActiveEdge) : bool =
        ae === ae.outrec.frontEdge

    let inline getDx (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float) : float =
        let dy = pt2Y - pt1Y
        if dy <> 0.0 then
            (pt2X - pt1X) / dy
        elif pt2X > pt1X then
            Double.NegativeInfinity
        else
            Double.PositiveInfinity

    let inline setDx (ae: ActiveEdge) : unit =
        ae.dx <- getDx (ae.botX, ae.botY, ae.topX, ae.topY)

    let inline topX (ae: ActiveEdge, currentY: float) : float =
        if currentY = ae.topY || ae.topX = ae.botX then
            ae.topX
        elif currentY = ae.botY then
            ae.botX
        else
            // .NET Math.Round already uses banker's rounding (roundToEven) by default.
            jsRound (ae.botX + ae.dx * (currentY - ae.botY))

    let inline isHorizontal (ae: ActiveEdge) : bool =
        ae.topY = ae.botY

    let inline isHeadingRightHorz (ae: ActiveEdge) : bool =
        ae.dx = Double.NegativeInfinity

    let inline isHeadingLeftHorz (ae: ActiveEdge) : bool =
        ae.dx = Double.PositiveInfinity

    let inline isSamePolyType (ae1: ActiveEdge, ae2: ActiveEdge) : bool =
        ae1.localMin.pathType = ae2.localMin.pathType

    let inline nextVertex (ae: ActiveEdge) : Vertex =
        if ae.windDx > 0 then
            ae.vertexTop.next
        else
            ae.vertexTop.prev

    let inline prevPrevVertex (ae: ActiveEdge) : Vertex =
        if ae.windDx > 0 then
            ae.vertexTop.prev.prev
        else
            ae.vertexTop.next.next

    let inline isMaximaV (v: Vertex) : bool =
        v.flags &&& VertexFlags.LocalMax <> VertexFlags.None

    let inline isMaximaA (ae: ActiveEdge) : bool =
        isMaximaV ae.vertexTop

    let getMaximaPair (ae: ActiveEdge) : ActiveEdge =
        let mutable ae2 = ae.nextInAEL
        let mutable result: ActiveEdge = null'()
        let mutable loopOn = true
        while loopOn && isNotNull ae2 do
            if ae2.vertexTop === ae.vertexTop then
                result <- ae2
                loopOn <- false
            else
                ae2 <- ae2.nextInAEL
        result

    let inline ptsReallyClose (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float) : bool =
        Math.Abs(pt1X - pt2X) < 2.0 && Math.Abs(pt1Y - pt2Y) < 2.0

    let isVerySmallTriangle (op: OutPt) : bool =
        op.next.next === op.prev &&
        (ptsReallyClose (op.prev.x, op.prev.y, op.next.x, op.next.y) ||
         ptsReallyClose (op.x, op.y, op.next.x, op.next.y) ||
         ptsReallyClose (op.x, op.y, op.prev.x, op.prev.y))

    /// Signed double-area of a closed OutPt ring.
    let areaOutPt (op: OutPt) : float =
        let mutable area = 0.0
        let mutable op2 = op
        let mutable loopOn = true
        while loopOn do
            let prev = op2.prev
            area <- area + (prev.y + op2.y) * (prev.x - op2.x)
            op2 <- op2.next
            if op2 === op then
                loopOn <- false
        area

    let inline areaTriangle (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float, pt3X: float, pt3Y: float) : float =
        (pt3Y + pt1Y) * (pt3X - pt1X) +
        (pt1Y + pt2Y) * (pt1X - pt2X) +
        (pt2Y + pt3Y) * (pt2X - pt3X)

    /// Fast bounding-box overlap test used before calling segsIntersect.
    let inline boundingBoxesOverlap (p1X: float, p1Y: float, p2X: float, p2Y: float, p3X: float, p3Y: float, p4X: float, p4Y: float) : bool =
        let min1x = Math.Min(p1X, p2X)
        let max1x = Math.Max(p1X, p2X)
        let min1y = Math.Min(p1Y, p2Y)
        let max1y = Math.Max(p1Y, p2Y)
        let min2x = Math.Min(p3X, p4X)
        let max2x = Math.Max(p3X, p4X)
        let min2y = Math.Min(p3Y, p4Y)
        let max2y = Math.Max(p3Y, p4Y)
        not (max1x < min2x || max2x < min1x || max1y < min2y || max2y < min1y)

    let buildPath (op: OutPt, reverse: bool, isOpen: bool, path: Path64) : bool =
        if isNull' op || op === op.next || (not isOpen && op.next === op.prev) then
            false
        else
            path.Clear()
            let mutable opL = op
            let mutable lastX = 0.0
            let mutable lastY = 0.0
            let mutable op2: OutPt = null'()
            if reverse then
                lastX <- opL.x
                lastY <- opL.y
                op2 <- opL.prev
            else
                opL <- opL.next
                lastX <- opL.x
                lastY <- opL.y
                op2 <- opL.next
            path.Add(lastX, lastY, opL.z)

            while op2 =!= opL do
                if xyNotEqual(op2.x, op2.y, lastX, lastY) then
                    lastX <- op2.x
                    lastY <- op2.y
                    path.Add(lastX, lastY, op2.z)
                if reverse then
                    op2 <- op2.prev
                else
                    op2 <- op2.next

            path.Count <> 3 || isOpen || not (isVerySmallTriangle op2)


    let inline containsRect (rect:OutRec, rec_:OutRec) : bool =
        rec_.boundsLeft >= rect.boundsLeft && rec_.boundsRight  <= rect.boundsRight &&
        rec_.boundsTop  >= rect.boundsTop  && rec_.boundsBottom <= rect.boundsBottom

    /// actually this does set bounds
    let getBounds (outRec:OutRec) : unit =
        if outRec.path.Count > 0 then
            let xs = outRec.path.Xs
            let ys = outRec.path.Ys
            let mutable left = Double.MaxValue
            let mutable top = Double.MaxValue
            let mutable right = Double.MinValue
            let mutable bottom = Double.MinValue
            for i = 0 to xs.Count - 1 do
                let x = xs[i]
                let y = ys[i]
                if x < left   then left <- x
                if x > right  then right <- x
                if y < top    then top <- y
                if y > bottom then bottom <- y
            outRec.boundsLeft <- left
            outRec.boundsTop <- top
            outRec.boundsRight <- right
            outRec.boundsBottom <- bottom

    let inline isEmptyRect (outRec:OutRec) : bool =
        outRec.boundsBottom <= outRec.boundsTop || outRec.boundsRight <= outRec.boundsLeft


    /// Perpendicular distance from pt to line (line1,line2) squared > 1/4 ?
    let perpendicDistFromLineSqrdGreaterThanQuarter (ptX: float, ptY: float, line1X: float, line1Y: float, line2X: float, line2Y: float) : bool =
        let a = ptX - line1X
        let b = ptY - line1Y
        let c = line2X - line1X
        let d = line2Y - line1Y
        if c = 0.0 && d = 0.0 then
            false
        else
            let cross = a * d - c * b
            (cross * cross) / (c*c + d*d) > 0.25

    let addLocMin (vert: Vertex, pathType: PathType, isOpen: bool, minimaList: ResizeArray<LocalMinima>) : unit =
        // make sure the vertex is added only once ...
        if (vert.flags &&& VertexFlags.LocalMin) <> VertexFlags.None then
            ()
        else
            vert.flags <- vert.flags ||| VertexFlags.LocalMin
            minimaList.Add  {vertex = vert; pathType = pathType; isOpen = isOpen}

    let addPathsToVertexList (paths: Paths64, pathType: PathType, isOpen: bool, minimaList: ResizeArray<LocalMinima>, vertexList: ResizeArray<Vertex>) : unit =
        for i = 0 to paths.Count - 1 do
            let path = paths[i]
            let mutable v0: Vertex = null'()
            let mutable prevV: Vertex = null'()
            let xs = path.Xs
            let ys = path.Ys
            let zs = path.Zs

            for j = 0 to path.Count - 1 do
                let x = xs[j]
                let y = ys[j]
                let z = zs[j]
                if isNull' v0 then
                    v0 <- { x = x; y = y; z = z; next = null'(); prev = null'(); flags = VertexFlags.None }
                    vertexList.Add(v0)
                    prevV <- v0
                elif xyNotEqual(prevV.x, prevV.y, x, y) then  // skip duplicates
                    let currV = { x = x; y = y; z = z; next = null'(); prev = prevV; flags = VertexFlags.None }
                    vertexList.Add currV
                    prevV.next <- currV
                    prevV <- currV

            if isNull' prevV || isNull' prevV.prev then
                () // continue
            else
                if not isOpen && xyEqual(prevV.x, prevV.y, v0.x, v0.y) then
                    prevV <- prevV.prev
                prevV.next <- v0
                v0.prev <- prevV
                if not isOpen && prevV.next === prevV then
                    () // continue
                else
                    // OK, we have a valid path
                    let mutable goingUp = false
                    let mutable skip = false
                    if isOpen then
                        let mutable currV = v0.next
                        while currV =!= v0 && currV.y = v0.y do
                            currV <- currV.next
                        goingUp <- currV.y <= v0.y
                        if goingUp then
                            v0.flags <- VertexFlags.OpenStart
                            addLocMin(v0, pathType, true, minimaList)
                        else
                            v0.flags <- VertexFlags.OpenStart ||| VertexFlags.LocalMax
                    else
                        // closed path
                        prevV <- v0.prev
                        while prevV =!= v0 && prevV.y = v0.y do
                            prevV <- prevV.prev
                        if prevV === v0 then
                            skip <- true // only open paths can be completely flat
                        else
                            goingUp <- prevV.y > v0.y

                    if not skip then
                        let goingUp0 = goingUp
                        prevV <- v0
                        let mutable currV = v0.next
                        while currV =!= v0 do
                            if currV.y > prevV.y && goingUp then
                                prevV.flags <- prevV.flags ||| VertexFlags.LocalMax
                                goingUp <- false
                            elif currV.y < prevV.y && (not goingUp) then
                                goingUp <- true
                                addLocMin(prevV, pathType, isOpen, minimaList)
                            prevV <- currV
                            currV <- currV.next

                        if isOpen then
                            prevV.flags <- prevV.flags ||| VertexFlags.OpenEnd
                            if goingUp then
                                prevV.flags <- prevV.flags ||| VertexFlags.LocalMax
                            else
                                addLocMin(prevV, pathType, isOpen, minimaList)
                        elif goingUp <> goingUp0 then
                            if goingUp0 then
                                addLocMin(prevV, pathType, false, minimaList)
                            else
                                prevV.flags <- prevV.flags ||| VertexFlags.LocalMax


    let intersectListSort: Comparison<IntersectNode> =
        Comparison<IntersectNode>(fun (a:IntersectNode) (b:IntersectNode) ->
            if   a.y <> b.y then
                if a.y > b.y then -1 else 1
            elif a.x <> b.x then
                if a.x < b.x then -1 else 1
            // Tiebreaker: when points are identical, sort by edge1's curX position
            // This provides deterministic ordering matching C# IntroSort behavior
            elif a.edge1.curX <> b.edge1.curX then
                if a.edge1.curX < b.edge1.curX then -1 else 1
            // Final tiebreaker: edge2's curX
            elif a.edge2.curX <  b.edge2.curX then
                -1
            elif a.edge2.curX >  b.edge2.curX then
                1
            else
                0
            )


    let horzSegSort: Comparison<HorzSegment> =
        Comparison<HorzSegment>(fun (hs1: HorzSegment) (hs2: HorzSegment) ->
            if isNull' hs1.rightOp then
                if isNull' hs2.rightOp then
                    0
                else
                    1
            elif isNull' hs2.rightOp then
                -1
            else
                let a = hs1.leftOp.x
                let b = hs2.leftOp.x
                if a < b then
                    -1
                elif a > b then
                    1
                else
                    0
            )


    let inline edgesAdjacentInAEL (inode: IntersectNode) : bool =
        inode.edge1.nextInAEL === inode.edge2 ||
        inode.edge1.prevInAEL === inode.edge2

    // ---- OutPt helpers ----

    let duplicateOp (op: OutPt, insertAfter: bool) : OutPt =
        let result = OutPt.create (op.x, op.y, op.z, op.outrec)
        if insertAfter then
            result.next <- op.next
            result.next.prev <- result
            result.prev <- op
            op.next <- result
        else
            result.prev <- op.prev
            result.prev.next <- result
            result.next <- op
            op.prev <- result
        result

    let inline disposeOutPt (op: OutPt) : OutPt =
        let result =
            if op.next === op then
                null'()
            else
                op.next
        op.prev.next <- op.next
        op.next.prev <- op.prev
        result

    let inline isValidClosedPath (op: OutPt) : bool =
        isNotNull op && op.next =!= op &&
        (op.next =!= op.prev || not (isVerySmallTriangle op))

    let inline outrecIsAscending (hotEdge: ActiveEdge) : bool =
        hotEdge === hotEdge.outrec.frontEdge

    // ---- OutRec helpers ----

    let getRealOutRec (outRec: OutRec) : OutRec =
        let mutable o = outRec
        while isNotNull o && isNull' o.pts do
            o <- o.owner
        o

    let inline setSides (outrec: OutRec, startEdge: ActiveEdge, endEdge: ActiveEdge) : unit =
        outrec.frontEdge <- startEdge
        outrec.backEdge <- endEdge

    let inline swapFrontBackSides (outrec: OutRec) : unit =
        let ae2 = outrec.frontEdge
        outrec.frontEdge <- outrec.backEdge
        outrec.backEdge <- ae2
        outrec.pts <- outrec.pts.next

    let setOwner (outrec: OutRec, newOwner: OutRec) : unit =
        let mutable owner = newOwner
        while isNotNull owner.owner && isNull' owner.owner.pts do
            owner.owner <- owner.owner.owner
        // Make sure outrec isn't an ancestor of newOwner
        let mutable tmp: OutRec = owner
        let mutable loopOn = true
        while loopOn && isNotNull tmp && tmp =!= outrec do
            tmp <- tmp.owner
            if isNull' tmp then
                loopOn <- false
        if isNotNull tmp then
            owner.owner <- outrec.owner
        outrec.owner <- owner

    let inline moveSplits (fromOr: OutRec, toOr: OutRec) : unit =
        if isNull' fromOr.splits then
            ()
        else
            if isNull' toOr.splits then
                toOr.splits <- ResizeArray<int>()
            for i=0 to fromOr.splits.Count - 1 do
                let idx = fromOr.splits[i]
                if idx <> toOr.idx then
                    toOr.splits.Add(idx)
            fromOr.splits <- null'()

    let inline fixOutRecPts (outrec: OutRec) : unit =
        let mutable op = outrec.pts
        let mutable loopOn = true
        while loopOn do
            op.outrec <- outrec
            op <- op.next
            if op === outrec.pts then
                loopOn <- false

    let inline uncoupleOutRec (ae: ActiveEdge) : unit =
        let outrec = ae.outrec
        if isNull' outrec then
            ()
        else
            outrec.frontEdge.outrec <- null'()
            outrec.backEdge.outrec <- null'()
            outrec.frontEdge <- null'()
            outrec.backEdge <- null'()


    let inline isValidOwner (outRec: OutRec, testOwnerIn: OutRec) : bool =
        let mutable testOwner = testOwnerIn
        while isNotNull testOwner && testOwner =!= outRec do
            testOwner <- testOwner.owner
        isNull' testOwner


    let inline getLastOp (hotEdge: ActiveEdge) : OutPt =
        let outrec = hotEdge.outrec
        if hotEdge === outrec.frontEdge then
            outrec.pts
        else
            outrec.pts.next


    // ---- Horizontal processing ----

    let trimHorz (horzEdge: ActiveEdge, preserveCollinearFlag: bool) : unit =
        let mutable wasTrimmed = false
        let mutable pt = nextVertex horzEdge
        let mutable loopOn = true
        while loopOn && pt.y = horzEdge.topY do
            // always trim 180 deg. spikes (in closed paths)
            // but otherwise break if preserveCollinear = true
            if preserveCollinearFlag && ((pt.x < horzEdge.topX) <> (horzEdge.botX < horzEdge.topX)) then
                loopOn <- false
            else
                horzEdge.vertexTop <- nextVertex horzEdge
                horzEdge.topX <- pt.x
                horzEdge.topY <- pt.y
                horzEdge.topZ <- pt.z
                wasTrimmed <- true
                if isMaximaA horzEdge then
                    loopOn <- false
                else
                    pt <- nextVertex horzEdge
        if wasTrimmed then
            setDx horzEdge

    let getCurrYMaximaVertex (ae: ActiveEdge) : Vertex =
        let mutable result = ae.vertexTop
        if ae.windDx > 0 then
            while result.next.y = result.y do
                result <- result.next
        else
            while result.prev.y = result.y do
                result <- result.prev
        if not (isMaximaV result) then
            null'()
        else
            result


    let getCurrYMaximaVertexOpen (ae: ActiveEdge) : Vertex =
        let mutable result = ae.vertexTop
        if ae.windDx > 0 then
            while result.next.y = result.y && (result.flags &&& (VertexFlags.OpenEnd ||| VertexFlags.LocalMax)) = VertexFlags.None do
                result <- result.next
        else
            while result.prev.y = result.y && (result.flags &&& (VertexFlags.OpenEnd ||| VertexFlags.LocalMax)) = VertexFlags.None do
                result <- result.prev
        if not (isMaximaV result) then
            null'()
        else
            result

//#endregion
//#region Clipper64

/// Merged Clipper executor for integer (Point64) paths.
/// The TypeScript base/concrete split is collapsed here because this F# port
/// omits the double-precision ClipperD hierarchy.
type Clipper64() =
    let mutable useScanlineArray = false
    let scanlineHeap = ScanlineHeap()
    let scanlineSet = HashSet<float>()

    let mutable cliptype = ClipType.NoClip
    let mutable fillrule = FillRule.EvenOdd
    let mutable actives: ActiveEdge = null'()
    let mutable sel: ActiveEdge = null'()
    let minimaList = ResizeArray<LocalMinima>()
    let intersectList = ResizeArray<IntersectNode>()
    let vertexList = ResizeArray<Vertex>()
    let outrecList = ResizeArray<OutRec>()
    let scanlineArr = ResizeArray<float>()
    let horzSegList = ResizeArray<HorzSegment>()
    let horzJoinList = ResizeArray<HorzJoin>()

    let mutable currentLocMin = 0
    let mutable currentBotY = 0.0
    let mutable isSortedMinimaList = false
    let mutable hasOpenPaths = false
    let mutable usingPolytree = false
    let mutable succeeded = false

    let mutable preserveCollinear = true
    let mutable reverseSolution = false
    let mutable zCallback: ZCallback64 option = None


    // Returns the intersection Z value, possibly updated by the callback.
    let setZ (ae1: ActiveEdge, ae2: ActiveEdge, opt:OutPt) : unit =
        match zCallback with
        | None -> ()
        | Some zcallback ->
            let mutable updatedZ = opt.z
            // prioritize subject vertices over clip vertices
            // and pass the subject vertices before clip vertices in the callback
            let intersectX = opt.x
            let intersectY = opt.y
            if ae1.localMin.pathType = PathType.Subject then
                if   Clip.xyEqual (intersectX, intersectY, ae1.botX, ae1.botY) then updatedZ <- ae1.botZ
                elif Clip.xyEqual (intersectX, intersectY, ae1.topX, ae1.topY) then updatedZ <- ae1.topZ
                elif Clip.xyEqual (intersectX, intersectY, ae2.botX, ae2.botY) then updatedZ <- ae2.botZ
                elif Clip.xyEqual (intersectX, intersectY, ae2.topX, ae2.topY) then updatedZ <- ae2.topZ
                else                                                                updatedZ <- Null.DEFZ
                opt.z <- zcallback( ae1, ae2, intersectX, intersectY, updatedZ)
            else
                if   Clip.xyEqual (intersectX, intersectY, ae2.botX, ae2.botY) then updatedZ <- ae2.botZ
                elif Clip.xyEqual (intersectX, intersectY, ae2.topX, ae2.topY) then updatedZ <- ae2.topZ
                elif Clip.xyEqual (intersectX, intersectY, ae1.botX, ae1.botY) then updatedZ <- ae1.botZ
                elif Clip.xyEqual (intersectX, intersectY, ae1.topX, ae1.topY) then updatedZ <- ae1.topZ
                else                                                                updatedZ <- Null.DEFZ
                opt.z <- zcallback(ae2, ae1, intersectX, intersectY, updatedZ)


    // ---- AEL management ----

    let deleteFromAEL (ae: ActiveEdge) : unit =
        let prev = ae.prevInAEL
        let next = ae.nextInAEL
        if isNull' prev && isNull' next && ae =!= actives then
            () // already deleted
        else
            if isNotNull prev then
                prev.nextInAEL <- next
            else
                actives <- next
            if isNotNull next then
                next.prevInAEL <- prev

    let clearSolutionOnly () : unit =
        while isNotNull actives do deleteFromAEL actives
        scanlineHeap.ClearData()
        scanlineSet.Clear()
        scanlineArr|> Rarr.clear
        intersectList|> Rarr.clear //disposeIntersectNodes()
        outrecList|> Rarr.clear
        horzSegList|> Rarr.clear
        horzJoinList|> Rarr.clear


    let insertScanline (y: float) : unit =
        if useScanlineArray then
            let mutable found = false
            let mutable i = 0
            while i < scanlineArr.Count && not found do
                if scanlineArr[i] = y then
                    found <- true
                else
                    i <- i + 1
            if not found then
                scanlineArr.Add(y)
                if scanlineArr.Count > 64 then
                    // upgradeScanlineStructureFromArray() inlined:
                    for i = 0 to scanlineArr.Count - 1 do
                        let y = scanlineArr[i]
                        scanlineSet.Add(y) |> ignore
                        scanlineHeap.Push(y)
                    scanlineArr|> Rarr.clear
                    useScanlineArray <- false
        else
            if not (scanlineSet.Contains(y)) then
                scanlineSet.Add(y) |> ignore
                scanlineHeap.Push(y)

    // Returns the next scanline Y coordinate, or NaN if there are no more.
    let popScanline () : float =
        if useScanlineArray then
            if scanlineArr.Count = 0 then
                Double.NaN
            else
                let len = scanlineArr.Count
                let mutable bestIdx = 0
                let mutable bestY = scanlineArr[0]
                for i = 1 to len - 1 do
                    let v = scanlineArr[i]
                    if v > bestY then
                        bestY <- v
                        bestIdx <- i
                scanlineArr[bestIdx] <- scanlineArr[len - 1]
                scanlineArr  |> Rarr.pop
                bestY
        else
            let y = scanlineHeap.Pop()
            if not (Double.IsNaN y) then
                scanlineSet.Remove y |> ignore
            y




    let minimaListCmp : Comparison<LocalMinima> =
        Comparison<LocalMinima>(fun (a:LocalMinima) (b:LocalMinima) ->
            let ya = a.vertex.y
            let yb = b.vertex.y
            if ya > yb then
                -1
            elif ya < yb then
                1
            else
                0
        )

    let reset () : unit =
        let minimaList = minimaList // avoiding access via 'this' in JS
        if not isSortedMinimaList then
            // printfn "Sorting minima list with %d items" minimaList.Count
            minimaList.Sort minimaListCmp
            isSortedMinimaList <- true

        scanlineHeap.ClearData()
        scanlineSet.Clear()
        scanlineArr|> Rarr.clear
        // Heuristic: local minima count correlates with number of scanlines and
        // scanline insert/pop activity. For glyph-like inputs, this is typically small.
        useScanlineArray <- minimaList.Count <= 16
        for i = minimaList.Count - 1 downto 0 do
            insertScanline minimaList[i].vertex.y

        currentBotY <- 0.0
        currentLocMin <- 0
        actives <- null'()
        sel <- null'()
        succeeded <- true


    // ---- OutRec helpers ----

    let newOutRec () : OutRec =
        let result : OutRec = {
            idx = 0
            owner = null'()
            frontEdge = null'()
            backEdge = null'()
            pts = null'()
            polypath = null'()
            // bounds = Rect64.createEmpty()
            boundsLeft = 0.0
            boundsTop = 0.0
            boundsRight = 0.0
            boundsBottom = 0.0
            path = Path64()
            isOpen = false
            splits = null'()
            recursiveSplit = null'()
        }
        result.idx <- outrecList.Count
        outrecList.Add(result)
        result


    // ---- Point in polygon ----

    let getCleanPath (op: OutPt) : Path64 =
        let result = Path64()
        let mutable op2 = op
        while op2.next =!= op && ((op2.x = op2.next.x && op2.x = op2.prev.x) || (op2.y = op2.next.y && op2.y = op2.prev.y)) do
            op2 <- op2.next
        result.Add(op2.x, op2.y, op2.z)
        let mutable prevOp = op2
        op2 <- op2.next
        while op2 =!= op do
            if (op2.x <> op2.next.x || op2.x <> prevOp.x) && (op2.y <> op2.next.y || op2.y <> prevOp.y) then
                result.Add(op2.x, op2.y, op2.z)
                prevOp <- op2
            op2 <- op2.next
        result

    let pointInOpPolygon (ptX: float) (ptY: float) (op: OutPt) : PointInPolygonResult =
        if op === op.next || op.prev === op.next then
            PointInPolygonResult.IsOutside
        else
            let mutable opL = op
            let mutable op2 = opL
            let mutable loopOn = true
            while loopOn do
                if opL.y <> ptY then
                    loopOn <- false
                else
                    opL <- opL.next
                    if opL === op2 then
                        loopOn <- false
            if opL.y = ptY then
                PointInPolygonResult.IsOutside
            else
                let mutable isAbove = opL.y < ptY
                let startingAbove = isAbove
                let mutable value = 0
                let mutable result = PointInPolygonResult.IsOutside
                let mutable settled = false
                let mutable op2b = opL.next
                while not settled && op2b =!= opL do
                    if isAbove then
                        while op2b =!= opL && op2b.y < ptY do
                            op2b <- op2b.next
                    else
                        while (op2b =!= opL) && op2b.y > ptY do
                            op2b <- op2b.next
                    if op2b === opL then
                        () // break outer
                    else
                        if op2b.y = ptY then
                            if op2b.x = ptX ||(op2b.y = op2b.prev.y && ((ptX < op2b.prev.x) <> (ptX < op2b.x))) then
                                result <- PointInPolygonResult.IsOn
                                settled <- true
                            else
                                op2b <- op2b.next
                                if op2b === opL then
                                    () // break
                        else
                            if op2b.x <= ptX || op2b.prev.x <= ptX then
                                if op2b.prev.x < ptX && op2b.x < ptX then
                                    value <- 1 - value
                                else
                                    let d = Geo.crossProductSign (op2b.prev.x, op2b.prev.y, op2b.x, op2b.y, ptX, ptY)
                                    if d = 0 then
                                        result <- PointInPolygonResult.IsOn
                                        settled <- true
                                    elif (d < 0) = isAbove then
                                        value <- 1 - value
                            if not settled then
                                isAbove <- not isAbove
                                op2b <- op2b.next

                if settled then
                    result
                elif isAbove = startingAbove then
                    if value = 0 then
                        PointInPolygonResult.IsOutside
                    else
                        PointInPolygonResult.IsInside
                else
                    let d = Geo.crossProductSign (op2b.prev.x, op2b.prev.y, op2b.x, op2b.y, ptX, ptY)
                    if d = 0 then
                        PointInPolygonResult.IsOn
                    else
                        if (d < 0) = isAbove then
                            value <- 1 - value
                        if value = 0 then
                            PointInPolygonResult.IsOutside
                        else
                            PointInPolygonResult.IsInside

    let path1InsidePath2 (op1: OutPt, op2: OutPt) : bool =
        // allow rounding noise: skip if the first vertex appears outside
        let mutable pip = PointInPolygonResult.IsOn
        let mutable op = op1
        let mutable result = false
        let mutable finished = false
        let mutable loopOn = true
        while loopOn do
            match pointInOpPolygon op.x op.y op2 with
            | PointInPolygonResult.IsOutside ->
                if pip = PointInPolygonResult.IsOutside then
                    result <- false
                    finished <- true
                    loopOn <- false
                else
                    pip <- PointInPolygonResult.IsOutside
            | PointInPolygonResult.IsInside ->
                if pip = PointInPolygonResult.IsInside then
                    result <- true
                    finished <- true
                    loopOn <- false
                else
                    pip <- PointInPolygonResult.IsInside
            | _ -> ()
            if loopOn then
                op <- op.next
                if op === op1 then
                    loopOn <- false
        if finished then
            result
        else
            Geo.path2ContainsPath1 (getCleanPath op1) (getCleanPath op2)

    // ---- Horizontal segments / joins ----

    let setHorzSegHeadingForward (hs: HorzSegment, opP: OutPt, opN: OutPt) : bool =
        if opP.x = opN.x then
            false
        else
            if opP.x < opN.x then
                hs.leftOp <- opP
                hs.rightOp <- opN
                hs.leftToRight <- true
            else
                hs.leftOp <- opN
                hs.rightOp <- opP
                hs.leftToRight <- false
            true

    let updateHorzSegment (hs: HorzSegment) : bool =
        let op = hs.leftOp
        let outrec = Clip.getRealOutRec op.outrec
        let outrecHasEdges = isNotNull outrec.frontEdge
        let currY = op.y
        let mutable opP = op
        let mutable opN = op

        if outrecHasEdges then
            let opA = outrec.pts
            let opZ = opA.next
            while opP =!= opZ && opP.prev.y = currY do
                opP <- opP.prev
            while opN =!= opA && opN.next.y = currY do
                opN <- opN.next
        else
            while opP.prev =!= opN && opP.prev.y = currY do
                opP <- opP.prev
            while opN.next =!= opP && opN.next.y = currY do
                opN <- opN.next

        let result = setHorzSegHeadingForward(hs, opP, opN) && isNull' hs.leftOp.horz

        if result then
            hs.leftOp.horz <- hs
        else
            hs.rightOp <- null'() // (for sorting)
        result


    // ---- Edges / adding output points ----

    let addOutPt (ae: ActiveEdge, ptX: float, ptY: float, ptZ: obj) : OutPt =
        let outrec = ae.outrec
        let toFront = Clip.isFront ae
        let opFront = outrec.pts
        let opBack = opFront.next

        if toFront && Clip.xyEqual(ptX, ptY, opFront.x, opFront.y) then
            opFront
        elif not toFront && Clip.xyEqual(ptX, ptY, opBack.x, opBack.y) then
            opBack
        else
            let newOp = OutPt.create (ptX, ptY, ptZ, outrec)
            opBack.prev <- newOp
            newOp.prev <- opFront
            newOp.next <- opBack
            opFront.next <- newOp
            if toFront then
                outrec.pts <- newOp
            newOp

    let addToHorzSegList (op: OutPt) : unit =
        if op.outrec.isOpen then
            ()
        else
            horzSegList.Add {
                    HorzSegment.leftOp = op
                    HorzSegment.rightOp = null'()
                    HorzSegment.leftToRight = true
                }

    let startOpenPath (ae: ActiveEdge, ptX: float, ptY: float, ptZ: obj) : OutPt =
        let outrec = newOutRec()
        outrec.isOpen <- true
        if ae.windDx > 0 then
            outrec.frontEdge <- ae
            outrec.backEdge <- null'()
        else
            outrec.frontEdge <- null'()
            outrec.backEdge <- ae
        ae.outrec <- outrec
        let op = OutPt.create (ptX, ptY, ptZ, outrec)
        outrec.pts <- op
        op


    let addLocalMinPoly (ae1: ActiveEdge, ae2: ActiveEdge, ptX: float, ptY: float, ptZ: obj, isNew: bool) : OutPt =
        let outrec = newOutRec()
        ae1.outrec <- outrec
        ae2.outrec <- outrec

        if Clip.isOpen ae1 then
            outrec.owner <- null'()
            outrec.isOpen <- true
            if ae1.windDx > 0 then
                Clip.setSides(outrec, ae1, ae2)
            else
                Clip.setSides(outrec, ae2, ae1)
        else
            outrec.isOpen <- false
            let prevHotEdge = Clip.getPrevHotEdge ae1
            if isNotNull prevHotEdge then
                if usingPolytree then
                    Clip.setOwner(outrec, prevHotEdge.outrec)
                outrec.owner <- prevHotEdge.outrec
                if Clip.outrecIsAscending prevHotEdge = isNew then
                    Clip.setSides(outrec, ae2, ae1)
                else
                    Clip.setSides(outrec, ae1, ae2)
            else
                outrec.owner <- null'()
                if isNew then
                    Clip.setSides(outrec, ae1, ae2)
                else
                    Clip.setSides(outrec, ae2, ae1)

        let op = OutPt.create (ptX, ptY, ptZ, outrec)
        outrec.pts <- op
        op

    let joinOutrecPaths (ae1: ActiveEdge, ae2: ActiveEdge) : unit =
        let p1Start = ae1.outrec.pts
        let p2Start = ae2.outrec.pts
        let p1End = p1Start.next
        let p2End = p2Start.next
        if Clip.isFront ae1 then
            p2End.prev <- p1Start
            p1Start.next <- p2End
            p2Start.next <- p1End
            p1End.prev <- p2Start
            ae1.outrec.pts <- p2Start
            ae1.outrec.frontEdge <- ae2.outrec.frontEdge
            if isNotNull ae1.outrec.frontEdge then
                ae1.outrec.frontEdge.outrec <- ae1.outrec
        else
            p1End.prev <- p2Start
            p2Start.next <- p1End
            p1Start.next <- p2End
            p2End.prev <- p1Start
            ae1.outrec.backEdge <- ae2.outrec.backEdge
            if isNotNull ae1.outrec.backEdge then
                ae1.outrec.backEdge.outrec <- ae1.outrec

        ae2.outrec.frontEdge <- null'()
        ae2.outrec.backEdge <- null'()
        ae2.outrec.pts <- null'()
        Clip.setOwner(ae2.outrec, ae1.outrec)

        if Clip.isOpenEnd ae1 then
            ae2.outrec.pts <- ae1.outrec.pts
            ae1.outrec.pts <- null'()

        ae1.outrec <- null'()
        ae2.outrec <- null'()

    let swapOutrecs (ae1: ActiveEdge, ae2: ActiveEdge) : unit =
        let or1 = ae1.outrec
        let or2 = ae2.outrec
        if or1 === or2 then
            let ae = or1.frontEdge
            or1.frontEdge <- or1.backEdge
            or1.backEdge <- ae
        else
            if isNotNull or1 then
                if ae1 === or1.frontEdge then
                    or1.frontEdge <- ae2
                else
                    or1.backEdge <- ae2
            if isNotNull or2 then
                if ae2 === or2.frontEdge then
                    or2.frontEdge <- ae1
                else
                    or2.backEdge <- ae1
            ae1.outrec <- or2
            ae2.outrec <- or1

    let isJoined (e: ActiveEdge) : bool =
        e.joinWith <> JoinWith.None

    let split (e: ActiveEdge, currX: float, currY: float, currZ: obj) : unit =
        if e.joinWith = JoinWith.Right then
            e.joinWith <- JoinWith.None
            e.nextInAEL.joinWith <- JoinWith.None
            addLocalMinPoly(e, e.nextInAEL, currX, currY, currZ, true) |> ignore
        else
            e.joinWith <- JoinWith.None
            e.prevInAEL.joinWith <- JoinWith.None
            addLocalMinPoly(e.prevInAEL, e, currX, currY, currZ, true) |> ignore

    let addLocalMaxPoly (ae1: ActiveEdge, ae2: ActiveEdge, ptX: float, ptY: float, ptZ: obj) : OutPt =
        if isJoined ae1 then
            split(ae1, ptX, ptY, ptZ)
        if isJoined ae2 then
            split(ae2, ptX, ptY, ptZ)

        let mutable cancel = false
        if Clip.isFront ae1 = Clip.isFront ae2 then
            if Clip.isOpenEnd ae1 then
                Clip.swapFrontBackSides ae1.outrec
            elif Clip.isOpenEnd ae2 then
                Clip.swapFrontBackSides ae2.outrec
            else
                succeeded <- false
                cancel <- true

        if cancel then
            null'()
        else
            let result = addOutPt(ae1, ptX, ptY, ptZ)
            if ae1.outrec === ae2.outrec then
                let outrec = ae1.outrec
                outrec.pts <- result
                if usingPolytree then
                    let e = Clip.getPrevHotEdge ae1
                    if isNull' e then
                        outrec.owner <- null'()
                    else
                        Clip.setOwner(outrec, e.outrec)
                Clip.uncoupleOutRec ae1
            elif Clip.isOpen ae1 then
                if ae1.windDx < 0 then
                    joinOutrecPaths(ae1, ae2)
                else
                    joinOutrecPaths(ae2, ae1)
            elif ae1.outrec.idx < ae2.outrec.idx then
                joinOutrecPaths(ae1, ae2)
            else
                joinOutrecPaths(ae2, ae1)
            result

    // ---- AEL insertion / validation ----

    let rec isValidAelOrder (resident: ActiveEdge, newcomer: ActiveEdge) : bool =
        if newcomer.curX <> resident.curX then
            newcomer.curX > resident.curX
        else
            let d = Geo.crossProductSign (resident.topX, resident.topY, newcomer.botX, newcomer.botY, newcomer.topX, newcomer.topY)
            if d <> 0 then
                d < 0
            else
                if (not (Clip.isMaximaA resident)) && resident.topY > newcomer.topY then
                    let nextResident = Clip.nextVertex resident
                    Geo.crossProductSign (newcomer.botX, newcomer.botY, resident.topX, resident.topY, nextResident.x, nextResident.y) <= 0
                elif (not (Clip.isMaximaA newcomer)) && newcomer.topY > resident.topY then
                    let nextNewcomer = Clip.nextVertex newcomer
                    Geo.crossProductSign (newcomer.botX, newcomer.botY, newcomer.topX, newcomer.topY, nextNewcomer.x, nextNewcomer.y) >= 0
                else
                    let y = newcomer.botY
                    let newcomerIsLeft = newcomer.isLeftBound
                    if resident.botY <> y || resident.localMin.vertex.y <> y then
                        newcomer.isLeftBound
                    elif resident.isLeftBound <> newcomerIsLeft then
                        newcomerIsLeft
                    elif Geo.isCollinear ((Clip.prevPrevVertex resident).x, (Clip.prevPrevVertex resident).y, resident.botX, resident.botY, resident.topX, resident.topY) then
                        true
                    else
                        let cross = Geo.crossProductSign(
                                        (Clip.prevPrevVertex resident).x,
                                        (Clip.prevPrevVertex resident).y,
                                        newcomer.botX,
                                        newcomer.botY,
                                        (Clip.prevPrevVertex newcomer).x,
                                        (Clip.prevPrevVertex newcomer).y)
                        cross > 0  = newcomerIsLeft

    let insertLeftEdge (ae: ActiveEdge) : unit =
        if isNull' actives then
            ae.prevInAEL <- null'()
            ae.nextInAEL <- null'()
            actives <- ae
        elif not (isValidAelOrder(actives, ae)) then
            ae.prevInAEL <- null'()
            ae.nextInAEL <- actives
            actives.prevInAEL <- ae
            actives <- ae
        else
            let mutable ae2 = actives
            while isNotNull ae2.nextInAEL && isValidAelOrder(ae2.nextInAEL, ae) do
                ae2 <- ae2.nextInAEL
            if ae2.joinWith = JoinWith.Right then
                ae2 <- ae2.nextInAEL
            ae.nextInAEL <- ae2.nextInAEL
            if isNotNull ae2.nextInAEL then
                ae2.nextInAEL.prevInAEL <- ae
            ae.prevInAEL <- ae2
            ae2.nextInAEL <- ae

    let insertRightEdge (ae1: ActiveEdge, ae2: ActiveEdge) : unit =
        ae2.nextInAEL <- ae1.nextInAEL
        if isNotNull ae1.nextInAEL then
            ae1.nextInAEL.prevInAEL <- ae2
        ae2.prevInAEL <- ae1
        ae1.nextInAEL <- ae2

    let swapPositionsInAEL (ae1: ActiveEdge, ae2: ActiveEdge) : unit =
        let next = ae2.nextInAEL
        if isNotNull next then
            next.prevInAEL <- ae1
        let prev = ae1.prevInAEL
        if isNotNull prev then
            prev.nextInAEL <- ae2
        ae2.prevInAEL <- prev
        ae2.nextInAEL <- ae1
        ae1.prevInAEL <- ae2
        ae1.nextInAEL <- next
        if isNull' ae2.prevInAEL then
            actives <- ae2

    let pushHorz (ae: ActiveEdge) : unit =
        ae.nextInSEL <- sel
        sel <- ae

    let popHorz () : ActiveEdge =
        if isNull' sel then
            null'()
        else
            let ae = sel
            sel <- sel.nextInSEL
            ae

    // ---- Wind counts ----

    let setWindCountForOpenPathEdge (ae: ActiveEdge) : unit =
        let mutable ae2 = actives
        if fillrule = FillRule.EvenOdd then
            let mutable cnt1 = 0
            let mutable cnt2 = 0
            while ae2 =!= ae do
                if ae2.localMin.pathType = PathType.Clip then
                    cnt2 <- cnt2 + 1
                elif not (Clip.isOpen ae2) then
                    cnt1 <- cnt1 + 1
                ae2 <- ae2.nextInAEL
            ae.windCount  <- (if Clip.isOdd cnt1 then 1 else 0)
            ae.windCount2 <- (if Clip.isOdd cnt2 then 1 else 0)
        else
            while ae2 =!= ae do
                if ae2.localMin.pathType = PathType.Clip then
                    ae.windCount2 <- ae.windCount2 + ae2.windDx
                elif not (Clip.isOpen ae2) then
                    ae.windCount <- ae.windCount + ae2.windDx
                ae2 <- ae2.nextInAEL

    let setWindCountForClosedPathEdge (ae: ActiveEdge) : unit =
        let mutable ae2 = ae.prevInAEL
        let pt = ae.localMin.pathType

        if not Clip.state.openPathsEnabled then
            while isNotNull ae2 && ae2.localMin.pathType <> pt do
                ae2 <- ae2.prevInAEL

            if isNull' ae2 then
                ae.windCount <- ae.windDx
                ae2 <- actives
            elif fillrule = FillRule.EvenOdd then
                ae.windCount <- ae.windDx
                ae.windCount2 <- ae2.windCount2
                ae2 <- ae2.nextInAEL
            else
                if ae2.windCount * ae2.windDx < 0 then
                    if Math.Abs(ae2.windCount) > 1 then
                        if ae2.windDx * ae.windDx < 0 then
                            ae.windCount <- ae2.windCount
                        else
                            ae.windCount <- ae2.windCount + ae.windDx
                    else
                        ae.windCount <- ae.windDx
                else
                    if ae2.windDx * ae.windDx < 0 then
                        ae.windCount <- ae2.windCount
                    else
                        ae.windCount <- ae2.windCount + ae.windDx
                ae.windCount2 <- ae2.windCount2
                ae2 <- ae2.nextInAEL

            if fillrule = FillRule.EvenOdd then
                while ae2 =!= ae do
                    if ae2.localMin.pathType <> pt then
                        ae.windCount2 <- (if ae.windCount2 = 0 then 1 else 0)
                    ae2 <- ae2.nextInAEL
            else
                while ae2 =!= ae do
                    if ae2.localMin.pathType <> pt then
                        ae.windCount2 <- ae.windCount2 + ae2.windDx
                    ae2 <- ae2.nextInAEL
        else
            while isNotNull ae2 && (ae2.localMin.pathType <> pt || Clip.isOpen ae2) do
                ae2 <- ae2.prevInAEL

            if isNull' ae2 then
                ae.windCount <- ae.windDx
                ae2 <- actives
            elif fillrule = FillRule.EvenOdd then
                ae.windCount <- ae.windDx
                ae.windCount2 <- ae2.windCount2
                ae2 <- ae2.nextInAEL
            else
                if ae2.windCount * ae2.windDx < 0 then
                    if Math.Abs(ae2.windCount) > 1 then
                        if ae2.windDx * ae.windDx < 0 then
                            ae.windCount <- ae2.windCount
                        else
                            ae.windCount <- ae2.windCount + ae.windDx
                    else
                        ae.windCount <- (if Clip.isOpen ae then 1 else ae.windDx)
                else
                    if ae2.windDx * ae.windDx < 0 then
                        ae.windCount <- ae2.windCount
                    else
                        ae.windCount <- ae2.windCount + ae.windDx
                ae.windCount2 <- ae2.windCount2
                ae2 <- ae2.nextInAEL

            if fillrule = FillRule.EvenOdd then
                while ae2 =!= ae do
                    if ae2.localMin.pathType <> pt && not (Clip.isOpen ae2) then
                        ae.windCount2 <- (if ae.windCount2 = 0 then 1 else 0)
                    ae2 <- ae2.nextInAEL
            else
                while ae2 =!= ae do
                    if ae2.localMin.pathType <> pt && not (Clip.isOpen ae2) then
                        ae.windCount2 <- ae.windCount2 + ae2.windDx
                    ae2 <- ae2.nextInAEL

    let isContributingOpen (ae: ActiveEdge) : bool =
        let mutable isInClip = false
        let mutable isInSubj = false
        match fillrule with
        | FillRule.Positive ->
            isInSubj <- ae.windCount > 0
            isInClip <- ae.windCount2 > 0
        | FillRule.Negative ->
            isInSubj <- ae.windCount < 0
            isInClip <- ae.windCount2 < 0
        | _ ->
            isInSubj <- ae.windCount <> 0
            isInClip <- ae.windCount2 <> 0

        match cliptype with
        | ClipType.Intersection -> isInClip
        | ClipType.Union -> (not isInSubj) && (not isInClip)
        | _ -> not isInClip

    let isContributingClosed (ae: ActiveEdge) : bool =
        let mutable ok = true
        match fillrule with
        | FillRule.Positive -> if ae.windCount           <>  1 then ok <- false
        | FillRule.Negative -> if ae.windCount           <> -1 then ok <- false
        | FillRule.NonZero  -> if Math.Abs(ae.windCount) <>  1 then ok <- false
        | _ -> ()

        if not ok then false
        else
            match cliptype with
            | ClipType.Intersection ->
                if   fillrule = FillRule.Positive then ae.windCount2 > 0
                elif fillrule = FillRule.Negative then ae.windCount2 < 0
                else                                   ae.windCount2 <> 0

            | ClipType.Union ->
                if   fillrule = FillRule.Positive then ae.windCount2 <= 0
                elif fillrule = FillRule.Negative then ae.windCount2 >= 0
                else                                   ae.windCount2 = 0

            | ClipType.Difference ->
                let result =
                    if   fillrule = FillRule.Positive then ae.windCount2 <= 0
                    elif fillrule = FillRule.Negative then ae.windCount2 >= 0
                    else                                   ae.windCount2 = 0
                if ae.localMin.pathType = PathType.Subject then
                    result
                else
                    not result

            | ClipType.Xor -> true
            | _ -> false

    // ---- Check joins ----

    let checkJoinLeft (ae: ActiveEdge, ptX: float, ptY: float, ptZ: obj, checkCurrX: bool) : unit =
        let prev = ae.prevInAEL
        if isNull' prev
            || not (Clip.isHotEdge ae)
            || not (Clip.isHotEdge prev)
            || Clip.isHorizontal ae
            || Clip.isHorizontal prev
            || Clip.isOpen ae
            || Clip.isOpen prev then
                 ()
        elif (ptY < ae.topY + 2.0 || ptY < prev.topY + 2.0) && (ae.botY > ptY || prev.botY > ptY) then
                ()
        else
            let earlyExit =
                if checkCurrX then
                    Clip.perpendicDistFromLineSqrdGreaterThanQuarter (ptX, ptY, prev.botX, prev.botY, prev.topX, prev.topY)
                else
                    ae.curX <> prev.curX
            if earlyExit then
                ()
            elif not (Geo.isCollinear (ae.topX, ae.topY, ptX, ptY, prev.topX, prev.topY)) then
                ()
            else
                if ae.outrec.idx = prev.outrec.idx then
                    addLocalMaxPoly(prev, ae, ptX, ptY, ptZ) |> ignore
                elif ae.outrec.idx < prev.outrec.idx then
                    joinOutrecPaths(ae, prev)
                else
                    joinOutrecPaths(prev, ae)
                prev.joinWith <- JoinWith.Right
                ae.joinWith <- JoinWith.Left

    let checkJoinRight (ae: ActiveEdge, ptX: float, ptY: float, ptZ: obj, checkCurrX: bool) : unit =
        let next = ae.nextInAEL
        if isNull' next
           || not (Clip.isHotEdge ae)
           || not (Clip.isHotEdge next)
           || Clip.isHorizontal ae
           || Clip.isHorizontal next
           || Clip.isOpen ae
           || Clip.isOpen next then
                ()
        elif (ptY < ae.topY + 2.0 || ptY < next.topY + 2.0) && (ae.botY > ptY || next.botY > ptY) then
                ()
        else
            let earlyExit =
                if checkCurrX then
                    Clip.perpendicDistFromLineSqrdGreaterThanQuarter (ptX, ptY, next.botX, next.botY, next.topX, next.topY)
                else
                    ae.curX <> next.curX
            if earlyExit then
                ()
            elif not (Geo.isCollinear (ae.topX, ae.topY, ptX, ptY, next.topX, next.topY)) then
                ()
            else
                if ae.outrec.idx = next.outrec.idx then
                    addLocalMaxPoly(ae, next, ptX, ptY, ptZ) |> ignore
                elif ae.outrec.idx < next.outrec.idx then
                    joinOutrecPaths(ae, next)
                else
                    joinOutrecPaths(next, ae)
                ae.joinWith <- JoinWith.Right
                next.joinWith <- JoinWith.Left

    // ---- Intersect edges ----

    let findEdgeWithMatchingLocMin (e: ActiveEdge) : ActiveEdge =
        let mutable result = e.nextInAEL
        let mutable loopOn = true
        while loopOn && isNotNull result do
            if isNotNull result.localMin && Clip.localMinimaEqual(result.localMin, e.localMin) then
                loopOn <- false
            elif not (Clip.isHorizontal result) &&
                 not (Clip.xyEqual(e.botX, e.botY, result.botX, result.botY)) then
                result <- null'()
            else
                result <- result.nextInAEL
        if isNotNull result then
            result
        else
            result <- e.prevInAEL
            let mutable loopOn = true
            let mutable finalResult: ActiveEdge = null'()
            while loopOn && isNotNull result do
                if isNotNull result.localMin && Clip.localMinimaEqual(result.localMin, e.localMin) then
                    finalResult <- result
                    loopOn <- false
                elif not (Clip.isHorizontal result) &&
                     not (Clip.xyEqual(e.botX, e.botY, result.botX, result.botY)) then
                    finalResult <- null'()
                    loopOn <- false
                else
                    result <- result.prevInAEL
            if not loopOn then
                finalResult
            else
                result

    let intersectEdges (ae1In: ActiveEdge, ae2In: ActiveEdge, ptX: float, ptY: float, ptZ: obj) : unit =
        let mutable ae1 = ae1In
        let mutable ae2 = ae2In
        let mutable resultOp: OutPt = null'()
        // MANAGE OPEN PATH INTERSECTIONS SEPARATELY ...
        if hasOpenPaths && (Clip.isOpen ae1 || Clip.isOpen ae2) then
            if Clip.isOpen ae1 && Clip.isOpen ae2 then
                ()
            else
                // the following line avoids duplicating quite a bit of code
                if Clip.isOpen ae2 then
                    let tmp = ae1
                    ae1 <- ae2
                    ae2 <- tmp
                if isJoined ae2 then
                    split(ae2, ptX, ptY, ptZ) // needed for safety

                let mutable cancel = false
                if cliptype = ClipType.Union then
                    if not (Clip.isHotEdge ae2) then
                        cancel <- true
                elif ae2.localMin.pathType = PathType.Subject then
                    cancel <- true

                if not cancel then
                    match fillrule with
                    | FillRule.Positive -> if ae2.windCount <>  1 then cancel <- true
                    | FillRule.Negative -> if ae2.windCount <> -1 then cancel <- true
                    | _ ->        if Math.Abs(ae2.windCount) <> 1 then cancel <- true

                if cancel then
                    ()
                else
                    // toggle contribution ...
                    if Clip.isHotEdge ae1 then
                        resultOp <- addOutPt(ae1, ptX, ptY, ptZ)
                        setZ(ae1, ae2, resultOp)
                        if Clip.isFront ae1 then
                            ae1.outrec.frontEdge <- null'()
                        else
                            ae1.outrec.backEdge <- null'()
                        ae1.outrec <- null'()
                    // horizontal edges can pass under open paths at a LocMins
                    elif ptX = ae1.localMin.vertex.x && ptY = ae1.localMin.vertex.y && not (Clip.isOpenEndVertex ae1.localMin.vertex) then
                        // find the other side of the LocMin and
                        // if it's 'hot' join up with it ...
                        let ae3 = findEdgeWithMatchingLocMin ae1
                        if isNotNull ae3 && Clip.isHotEdge ae3 then
                            ae1.outrec <- ae3.outrec
                            if ae1.windDx > 0 then
                                Clip.setSides(ae3.outrec, ae1, ae3)
                            else
                                Clip.setSides(ae3.outrec, ae3, ae1)
                        else
                            resultOp <- startOpenPath(ae1, ptX, ptY, ptZ)
                    else
                        resultOp <- startOpenPath(ae1, ptX, ptY, ptZ)
                    if isNotNull resultOp then
                        setZ(ae1, ae2, resultOp)
        else
            // MANAGING CLOSED PATHS FROM HERE ON

            if isJoined ae1 then
                split(ae1, ptX, ptY, ptZ)
            if isJoined ae2 then
                split(ae2, ptX, ptY, ptZ)

            // UPDATE WINDING COUNTS...

            let mutable oldE1WindCount = 0
            let mutable oldE2WindCount = 0
            if ae1.localMin.pathType = ae2.localMin.pathType then
                if fillrule = FillRule.EvenOdd then
                    oldE1WindCount <- ae1.windCount
                    ae1.windCount <- ae2.windCount
                    ae2.windCount <- oldE1WindCount
                else
                    if ae1.windCount + ae2.windDx = 0 then
                        ae1.windCount <- -ae1.windCount
                    else
                        ae1.windCount <- ae1.windCount + ae2.windDx
                    if ae2.windCount - ae1.windDx = 0 then
                        ae2.windCount <- -ae2.windCount
                    else
                        ae2.windCount <- ae2.windCount - ae1.windDx
            else
                if fillrule <> FillRule.EvenOdd then
                    ae1.windCount2 <- ae1.windCount2 + ae2.windDx
                else
                    ae1.windCount2 <- if ae1.windCount2 = 0 then 1 else 0
                if fillrule <> FillRule.EvenOdd then
                    ae2.windCount2 <- ae2.windCount2 - ae1.windDx
                else
                    ae2.windCount2 <- if ae2.windCount2 = 0 then 1 else 0

            match fillrule with
            | FillRule.Positive ->
                oldE1WindCount <- ae1.windCount
                oldE2WindCount <- ae2.windCount
            | FillRule.Negative ->
                oldE1WindCount <- -ae1.windCount
                oldE2WindCount <- -ae2.windCount
            | _ ->
                oldE1WindCount <- Math.Abs ae1.windCount
                oldE2WindCount <- Math.Abs ae2.windCount

            let e1WindCountIs0or1 = oldE1WindCount = 0 || oldE1WindCount = 1
            let e2WindCountIs0or1 = oldE2WindCount = 0 || oldE2WindCount = 1

            if not (Clip.isHotEdge ae1) && not e1WindCountIs0or1 || not (Clip.isHotEdge ae2) && not e2WindCountIs0or1 then
                ()

            elif Clip.isHotEdge ae1 && Clip.isHotEdge ae2 then
                // NOW PROCESS THE INTERSECTION ...

                // if both edges are 'hot' ...
                if (oldE1WindCount <> 0 && oldE1WindCount <> 1)
                    || (oldE2WindCount <> 0 && oldE2WindCount <> 1)
                    || (ae1.localMin.pathType <> ae2.localMin.pathType && cliptype <> ClipType.Xor) then
                        // this 'else if' condition isn't strictly needed but
                        // it's sensible to split polygons that only touch at
                        // a common vertex (not at common edges).
                        resultOp <- addLocalMaxPoly(ae1, ae2, ptX, ptY, ptZ)
                        if isNotNull resultOp then
                            setZ(ae1, ae2, resultOp)
                elif Clip.isFront ae1 || ae1.outrec === ae2.outrec then
                    resultOp <- addLocalMaxPoly(ae1, ae2, ptX, ptY, ptZ)
                    if isNotNull resultOp then
                        setZ(ae1, ae2, resultOp)
                    let op2 = addLocalMinPoly(ae1, ae2, ptX, ptY, ptZ, false)
                    setZ(ae1, ae2, op2)
                else
                    // can't treat as maxima & minima
                    resultOp <- addOutPt(ae1, ptX, ptY, ptZ)
                    setZ(ae1, ae2, resultOp)
                    let op2 = addOutPt(ae2, ptX, ptY, ptZ)
                    setZ(ae1, ae2, op2)
                    swapOutrecs(ae1, ae2)

            // if one or other edge is 'hot' ...
            elif Clip.isHotEdge ae1 then
                resultOp <- addOutPt(ae1, ptX, ptY, ptZ)
                setZ(ae1, ae2, resultOp)
                swapOutrecs(ae1, ae2)
            elif Clip.isHotEdge ae2 then
                resultOp <- addOutPt(ae2, ptX, ptY, ptZ)
                setZ(ae1, ae2, resultOp)
                swapOutrecs(ae1, ae2)
            else
                // neither edge is 'hot'
                let mutable e1Wc2 = 0
                let mutable e2Wc2 = 0
                match fillrule with
                | FillRule.Positive ->
                    e1Wc2 <- ae1.windCount2
                    e2Wc2 <- ae2.windCount2
                | FillRule.Negative ->
                    e1Wc2 <- -ae1.windCount2
                    e2Wc2 <- -ae2.windCount2
                | _ ->
                    e1Wc2 <- Math.Abs ae1.windCount2
                    e2Wc2 <- Math.Abs ae2.windCount2

                if not (Clip.isSamePolyType(ae1, ae2)) then
                    resultOp <- addLocalMinPoly(ae1, ae2, ptX, ptY, ptZ, false)
                    setZ(ae1, ae2, resultOp)
                elif oldE1WindCount = 1 && oldE2WindCount = 1 then
                    resultOp <- null'()
                    let mutable cancel = false
                    match cliptype with
                    | ClipType.Union ->
                        if e1Wc2 > 0 && e2Wc2 > 0 then
                            cancel <- true
                        else
                            resultOp <- addLocalMinPoly(ae1, ae2, ptX, ptY, ptZ, false)
                    | ClipType.Difference ->
                        if (ae1.localMin.pathType = PathType.Clip && e1Wc2 > 0 && e2Wc2 > 0) || (ae1.localMin.pathType = PathType.Subject && e1Wc2 <= 0 && e2Wc2 <= 0) then
                            resultOp <- addLocalMinPoly(ae1, ae2, ptX, ptY, ptZ, false)
                    | ClipType.Xor ->
                        resultOp <- addLocalMinPoly(ae1, ae2, ptX, ptY, ptZ, false)
                    | _ -> // ClipType.Intersection:
                        if e1Wc2 <= 0 || e2Wc2 <= 0 then
                            cancel <- true
                        else
                            resultOp <- addLocalMinPoly(ae1, ae2, ptX, ptY, ptZ, false)
                    if not cancel && isNotNull resultOp then
                        setZ(ae1, ae2, resultOp)

    // ---- SEL helpers ----

    let extractFromSEL (ae: ActiveEdge) : ActiveEdge =
        let res = ae.nextInSEL
        if isNotNull res then
            res.prevInSEL <- ae.prevInSEL
        ae.prevInSEL.nextInSEL <- res
        res

    let insert1Before2InSEL (ae1: ActiveEdge, ae2: ActiveEdge) : unit =
        ae1.prevInSEL <- ae2.prevInSEL
        if isNotNull ae1.prevInSEL then
            ae1.prevInSEL.nextInSEL <- ae1
        ae1.nextInSEL <- ae2
        ae2.prevInSEL <- ae1

    let adjustCurrXAndCopyToSEL (topY: float) : unit =
        let mutable ae = actives
        sel <- ae
        while isNotNull ae do
            ae.prevInSEL <- ae.prevInAEL
            ae.nextInSEL <- ae.nextInAEL
            ae.jump <- ae.nextInSEL
            ae.curX <- Clip.topX(ae, topY)
            ae <- ae.nextInAEL

    let addNewIntersectNode (ae1: ActiveEdge, ae2: ActiveEdge, topY: float) : unit =
        let xNode = Clip.getLineIntersectPt (ae1, ae2, topY)

        if xNode.y > currentBotY || xNode.y < topY then
            let absDx1 = Math.Abs(ae1.dx)
            let absDx2 = Math.Abs(ae2.dx)
            if absDx1 > 100.0 && absDx2 > 100.0 then
                if absDx1 > absDx2 then
                    Clip.getClosestPtOnSegment (xNode, ae1.botX, ae1.botY, ae1.topX, ae1.topY)
                else
                    Clip.getClosestPtOnSegment (xNode, ae2.botX, ae2.botY, ae2.topX, ae2.topY)
            elif absDx1 > 100.0 then
                Clip.getClosestPtOnSegment (xNode, ae1.botX, ae1.botY, ae1.topX, ae1.topY)
            elif absDx2 > 100.0 then
                Clip.getClosestPtOnSegment (xNode, ae2.botX, ae2.botY, ae2.topX, ae2.topY)
            else
                if xNode.y < topY then
                    xNode.y <- topY
                else
                    xNode.y <- currentBotY

                if absDx1 < absDx2 then
                    xNode.x <- Clip.topX(ae1, xNode.y)
                else
                    xNode.x <- Clip.topX(ae2, xNode.y)

        // In C# this copies pt (struct semantics), but our sole caller (addNewIntersectNode)
        // always passes a freshly-allocated point that goes out of scope immediately,
        // so we can take ownership directly and skip the copy.
        intersectList.Add xNode


    let buildIntersectList (topY: float) : bool =
        if isNull' actives || isNull' actives.nextInAEL then
            false
        else
            // Calculate edge positions at the top of the current scanbeam, and from this
            // we will determine the intersections required to reach these new positions.
            adjustCurrXAndCopyToSEL topY

            // Find all edge intersections in the current scanbeam using a stable merge
            // sort that ensures only adjacent edges are intersecting. Intersect info is
            // stored in intersectList ready to be processed in ProcessIntersectList.
            // Re merge sorts see https://stackoverflow.com/a/46319131/359538
            let mutable left = sel
            while isNotNull left && isNotNull left.jump do
                let mutable prevBase: ActiveEdge = null'()
                let mutable leftLoop = left
                while isNotNull leftLoop && isNotNull leftLoop.jump do
                    let mutable currBase = leftLoop
                    let mutable right = leftLoop.jump
                    let mutable lEnd = right
                    let rEnd = if isNotNull right then right.jump else null'()
                    leftLoop.jump <- rEnd

                    while leftLoop =!= lEnd && right =!= rEnd do
                        if right.curX < leftLoop.curX then
                            let mutable tmp = right.prevInSEL
                            let mutable inner = true
                            while inner do
                                addNewIntersectNode(tmp, right, topY)
                                if tmp === leftLoop then
                                    inner <- false
                                else
                                    tmp <- tmp.prevInSEL
                            tmp <- right
                            right <- extractFromSEL tmp
                            lEnd <- right
                            if isNotNull leftLoop then
                                insert1Before2InSEL(tmp, leftLoop)
                            if leftLoop =!= currBase then
                                () // continue
                            else
                                currBase <- tmp
                                currBase.jump <- rEnd
                                if isNull' prevBase then
                                    sel <- currBase
                                else
                                    prevBase.jump <- currBase
                        else
                            leftLoop <- leftLoop.nextInSEL

                    prevBase <- currBase
                    leftLoop <- rEnd
                left <- sel

            intersectList.Count > 0



    let processIntersectList () : unit =
        // We now have a list of intersections required so that edges will be
        // correctly positioned at the top of the scanbeam. However, it's important
        // that edge intersections are processed from the bottom up, but it's also
        // crucial that intersections only occur between adjacent edges.

        // First we do a quicksort so intersections proceed in a bottom up order ...
        // printfn $"sorting {intersectList.Count} intersections"
        intersectList.Sort Clip.intersectListSort

        for i = 0 to intersectList.Count - 1 do
            if not (Clip.edgesAdjacentInAEL(intersectList[i])) then
                let mutable j = i + 1
                while not (Clip.edgesAdjacentInAEL(intersectList[j])) do
                    j <- j + 1
                // swap positions i and j
                let tmp = intersectList[i]
                intersectList[i] <- intersectList[j]
                intersectList[j] <- tmp

            let node = intersectList[i]
            intersectEdges(node.edge1, node.edge2, node.x, node.y, node.z)
            swapPositionsInAEL(node.edge1, node.edge2)
            node.edge1.curX <- node.x
            node.edge2.curX <- node.x
            checkJoinLeft(node.edge2, node.x, node.y, node.z, true)
            checkJoinRight(node.edge1, node.x, node.y, node.z, true)

    let doIntersections (y: float) : unit =
        if buildIntersectList y then
            processIntersectList()
            intersectList|> Rarr.clear //disposeIntersectNodes()


    // ---- Horizontal processing ----

    let updateEdgeIntoAEL (ae: ActiveEdge) : unit =
        // Avoid copying points (and avoid materializing z=0) for speed.
        ae.botX <- ae.topX
        ae.botY <- ae.topY
        ae.botZ <- ae.topZ
        ae.vertexTop <- Clip.nextVertex ae
        ae.topX <- ae.vertexTop.x
        ae.topY <- ae.vertexTop.y
        ae.topZ <- ae.vertexTop.z
        ae.curX <- ae.botX
        Clip.setDx ae

        if isJoined ae then split(ae, ae.botX, ae.botY, ae.botZ)

        if Clip.isHorizontal ae then
            if not Clip.state.openPathsEnabled then
                // Closed-path-only fast path.
                Clip.trimHorz(ae, preserveCollinear)
            elif not (Clip.isOpen ae) then
                Clip.trimHorz(ae, preserveCollinear)
        else
            insertScanline ae.topY
            checkJoinLeft(ae, ae.botX, ae.botY, ae.botZ, false)
            checkJoinRight(ae, ae.botX, ae.botY, ae.botZ, true)

    let doHorizontal (horz: ActiveEdge) : unit =
        let horzIsOpen = Clip.isOpen horz
        let y = horz.botY

        let vertexMax =
            if horzIsOpen then
                Clip.getCurrYMaximaVertexOpen horz
            else
                Clip.getCurrYMaximaVertex horz

        // Inlined from `resetHorzDirection` to avoid tuple allocation:
        let mutable isLeftToRight = false
        let mutable leftX2 = 0.0
        let mutable rightX2 = 0.0
        if horz.botX = horz.topX then
            leftX2 <- horz.curX
            rightX2 <- horz.curX
            let mutable ae = horz.nextInAEL
            while isNotNull ae && ae.vertexTop =!= vertexMax do
                ae <- ae.nextInAEL
            isLeftToRight <- isNotNull ae
        elif horz.curX < horz.topX then
            isLeftToRight <- true
            leftX2 <- horz.curX
            rightX2 <- horz.topX
        else
            //isLeftToRight <- false
            leftX2 <- horz.topX
            rightX2 <- horz.curX


        if Clip.isHotEdge horz then
            let op = addOutPt(horz, horz.curX, y, Null.DEFZ)
            addToHorzSegList op

        let mutable outerLoop = true
        let mutable returned = false

        while outerLoop && not returned do
            let mutable ae =
                if isLeftToRight then
                    horz.nextInAEL
                else
                    horz.prevInAEL

            let mutable innerBreak = false
            while (not innerBreak) && isNotNull ae && not returned do
                if ae.vertexTop === vertexMax then
                    if Clip.isHotEdge horz && isJoined ae then
                        split(ae, ae.topX, ae.topY, ae.topZ)
                    if Clip.isHotEdge horz then
                        while horz.vertexTop =!= vertexMax do
                            addOutPt(horz, horz.topX, horz.topY, horz.topZ) |> ignore
                            updateEdgeIntoAEL horz
                        if isLeftToRight then
                            addLocalMaxPoly(horz, ae, horz.topX, horz.topY, horz.topZ) |> ignore
                        else
                            addLocalMaxPoly(ae, horz, horz.topX, horz.topY, horz.topZ) |> ignore
                    deleteFromAEL ae
                    deleteFromAEL horz
                    returned <- true
                else
                    let mutable localBreak = false
                    if (vertexMax =!= horz.vertexTop) || Clip.isOpenEnd horz then
                        if (isLeftToRight && ae.curX > rightX2) || ((not isLeftToRight) && ae.curX < leftX2) then
                            localBreak <- true
                        elif ae.curX = horz.topX && not (Clip.isHorizontal ae) then
                            let nextPt = Clip.nextVertex horz
                            if Clip.isOpen ae
                                && not (Clip.isSamePolyType(ae, horz))
                                && not (Clip.isHotEdge ae) then
                                    if (isLeftToRight
                                    && Clip.topX(ae, nextPt.y) > nextPt.x)
                                    || ((not isLeftToRight)
                                    && Clip.topX(ae, nextPt.y) < nextPt.x) then
                                            localBreak <- true
                            else
                                if (isLeftToRight
                                    && Clip.topX(ae, nextPt.y) >= nextPt.x)
                                    || (not isLeftToRight
                                    && Clip.topX(ae, nextPt.y) <= nextPt.x) then
                                        localBreak <- true

                    if localBreak then
                        innerBreak <- true
                    else
                        let ptX = ae.curX
                        let ptY = y
                        let ptZ = Null.DEFZ
                        if isLeftToRight then
                            intersectEdges(horz, ae, ptX, ptY, ptZ)
                            swapPositionsInAEL(horz, ae)
                            checkJoinLeft(ae, ptX, ptY, ptZ, false)
                            horz.curX <- ae.curX
                            ae <- horz.nextInAEL
                        else
                            intersectEdges(ae, horz, ptX, ptY, ptZ)
                            swapPositionsInAEL(ae, horz)
                            checkJoinRight(ae, ptX, ptY, ptZ, false)
                            horz.curX <- ae.curX
                            ae <- horz.prevInAEL

                        if Clip.isHotEdge horz then
                            addToHorzSegList (Clip.getLastOp horz)

            if not returned then
                if horzIsOpen && Clip.isOpenEnd horz then
                    if Clip.isHotEdge horz then
                        addOutPt(horz, horz.topX, horz.topY, horz.topZ) |> ignore
                        if Clip.isFront horz then
                            horz.outrec.frontEdge <- null'()
                        else
                            horz.outrec.backEdge <- null'()
                        horz.outrec <- null'()
                    deleteFromAEL horz
                    returned <- true
                elif (Clip.nextVertex horz).y <> horz.topY then
                    outerLoop <- false
                else
                    // still more horizontals in bound to process
                    if Clip.isHotEdge horz then
                        addOutPt(horz, horz.topX, horz.topY, horz.topZ) |> ignore
                    updateEdgeIntoAEL horz

                    // Inlined from `resetHorzDirection` to avoid tuple allocation:
                    if horz.botX = horz.topX then
                        leftX2 <- horz.curX
                        rightX2 <- horz.curX
                        let mutable ae = horz.nextInAEL
                        while isNotNull ae && ae.vertexTop =!= vertexMax do
                            ae <- ae.nextInAEL
                        isLeftToRight <- isNotNull ae
                    elif horz.curX < horz.topX then
                        isLeftToRight <- true
                        leftX2 <- horz.curX
                        rightX2 <- horz.topX
                    else
                        isLeftToRight <- false
                        leftX2 <- horz.topX
                        rightX2 <- horz.curX

        if not returned then
            if Clip.isHotEdge horz then
                let op = addOutPt(horz, horz.topX, horz.topY, horz.topZ)
                addToHorzSegList op
            updateEdgeIntoAEL horz

    // ---- Local minima insertion ----

    let insertLocalMinimaIntoAEL (botY: float) : unit =
        let minimaList = minimaList // avoiding access via 'this' in JS

        let inline hasLocMinAtY (y: float) : bool =
            currentLocMin < minimaList.Count &&
            minimaList[currentLocMin].vertex.y = y

        let inline popLocalMinima () : LocalMinima =
            let result = minimaList[currentLocMin]
            currentLocMin <- currentLocMin + 1
            result

        // Add any local minima (if any) at BotY ...
        // NB horizontal local minima edges should contain locMin.vertex.prev
        while hasLocMinAtY botY do
            let localMinima = popLocalMinima()
            let vertex = localMinima.vertex
            let mutable leftBound: ActiveEdge = null'()

            if (vertex.flags &&& VertexFlags.OpenStart) <> VertexFlags.None then
                leftBound <- null'()
            else
                leftBound <- ActiveEdge.create(
                    vertex.x,      // botX
                    vertex.y,      // botY
                    vertex.z,      // botZ
                    vertex.x,      // curX
                    -1,                        // windDx
                    vertex.prev,   // vertexTop
                    vertex.prev.x, // topX
                    vertex.prev.y, // topY
                    vertex.prev.z, // topZ
                    null'(),                   // outrec
                    localMinima)               // localMin
                // setDx is inlined in the Active constructor

            let mutable rightBound: ActiveEdge = null'()
            if (vertex.flags &&& VertexFlags.OpenEnd) <> VertexFlags.None then
                rightBound <- null'()
            else
                rightBound <- ActiveEdge.create(
                    vertex.x,       // botX
                    vertex.y,       // botY
                    vertex.z,       // botZ
                    vertex.x,       // curX
                    1,                          // windDx
                    vertex.next,    // vertexTop
                    vertex.next.x,  // topX
                    vertex.next.y,  // topY
                    vertex.next.z,  // topZ
                    null'(),                    // outrec
                    localMinima)                // localMin
                // setDx is inlined in the Active constructor


            if isNotNull leftBound && isNotNull rightBound then
                if Clip.isHorizontal leftBound then
                    if Clip.isHeadingRightHorz leftBound then
                        let tmp = leftBound
                        leftBound <- rightBound
                        rightBound <- tmp
                elif Clip.isHorizontal rightBound then
                    if Clip.isHeadingLeftHorz rightBound then
                        let tmp = leftBound
                        leftBound <- rightBound
                        rightBound <- tmp
                elif leftBound.dx < rightBound.dx then
                    let tmp = leftBound
                    leftBound <- rightBound
                    rightBound <- tmp
            elif isNull' leftBound then
                leftBound <- rightBound
                rightBound <- null'()

            let mutable contributing = false
            leftBound.isLeftBound <- true
            insertLeftEdge leftBound

            if not Clip.state.openPathsEnabled then
                setWindCountForClosedPathEdge leftBound
                contributing <- isContributingClosed leftBound
            elif Clip.isOpen leftBound then
                setWindCountForOpenPathEdge leftBound
                contributing <- isContributingOpen leftBound
            else
                setWindCountForClosedPathEdge leftBound
                contributing <- isContributingClosed leftBound

            if isNotNull rightBound then
                rightBound.windCount <- leftBound.windCount
                rightBound.windCount2 <- leftBound.windCount2
                insertRightEdge(leftBound, rightBound)

                if contributing then
                    addLocalMinPoly(leftBound, rightBound, leftBound.botX, leftBound.botY, leftBound.botZ, true) |> ignore
                    if not (Clip.isHorizontal leftBound) then
                        checkJoinLeft(leftBound, leftBound.botX, leftBound.botY, leftBound.botZ, false)

                while isNotNull rightBound.nextInAEL && isValidAelOrder(rightBound.nextInAEL, rightBound) do
                    intersectEdges(rightBound, rightBound.nextInAEL, rightBound.botX, rightBound.botY, rightBound.botZ)
                    swapPositionsInAEL(rightBound, rightBound.nextInAEL)

                if Clip.isHorizontal rightBound then
                    pushHorz rightBound
                else
                    checkJoinRight(rightBound, rightBound.botX, rightBound.botY, rightBound.botZ, false)
                    insertScanline rightBound.topY
            elif contributing && Clip.state.openPathsEnabled then
                startOpenPath(leftBound, leftBound.botX, leftBound.botY, leftBound.botZ) |> ignore

            if Clip.isHorizontal leftBound then
                pushHorz leftBound
            else
                insertScanline leftBound.topY

    // ---- Horz segments -> joins ----

    let convertHorzSegsToJoins () : unit =
        let horzSegList = horzSegList // avoiding access via 'this' in JS
        let mutable k = 0
        for i = 0 to horzSegList.Count - 1 do
            let hs = horzSegList[i]
            if updateHorzSegment hs then
                k <- k + 1
        if k < 2 then
            ()
        else

            horzSegList.Sort Clip.horzSegSort

            for i = 0 to k - 2 do
                let hs1 = horzSegList[i]
                for j = i + 1 to k - 1 do
                    let hs2 = horzSegList[j]
                    if    hs2.leftOp.x >= hs1.rightOp.x
                       || hs2.leftToRight = hs1.leftToRight
                       || hs2.rightOp.x <= hs1.leftOp.x then
                            ()
                    else
                        let currY = hs1.leftOp.y
                        if hs1.leftToRight then
                            while hs1.leftOp.next.y = currY && hs1.leftOp.next.x <= hs2.leftOp.x do
                                hs1.leftOp <- hs1.leftOp.next
                            while hs2.leftOp.prev.y = currY && hs2.leftOp.prev.x <= hs1.leftOp.x do
                                hs2.leftOp <- hs2.leftOp.prev
                            horzJoinList.Add{
                                        op1 = Clip.duplicateOp(hs1.leftOp, true)
                                        op2 = Clip.duplicateOp(hs2.leftOp, false) }
                        else
                            while hs1.leftOp.prev.y = currY && hs1.leftOp.prev.x <= hs2.leftOp.x do
                                hs1.leftOp <- hs1.leftOp.prev
                            while hs2.leftOp.next.y = currY && hs2.leftOp.next.x <= hs1.leftOp.x do
                                hs2.leftOp <- hs2.leftOp.next
                            horzJoinList.Add{
                                        op1 = Clip.duplicateOp(hs2.leftOp, true)
                                        op2 = Clip.duplicateOp(hs1.leftOp, false) }

    let processHorzJoins () : unit =
        let horzJoinList = horzJoinList // avoiding access via 'this' in JS
        for i = 0 to horzJoinList.Count - 1 do
            let hoj = horzJoinList[i]
            let or1 = Clip.getRealOutRec hoj.op1.outrec
            let or2 = Clip.getRealOutRec hoj.op2.outrec

            let op1b = hoj.op1.next
            let op2b = hoj.op2.prev
            hoj.op1.next <- hoj.op2
            hoj.op2.prev <- hoj.op1
            op1b.prev <- op2b
            op2b.next <- op1b

            if or1 === or2 then
                let or2New = newOutRec()
                or2New.pts <- op1b
                Clip.fixOutRecPts or2New

                if or1.pts.outrec === or2New then
                    or1.pts <- hoj.op1
                    or1.pts.outrec <- or1

                if usingPolytree then
                    if path1InsidePath2(or1.pts, or2New.pts) then
                        let tmp = or2New.pts
                        or2New.pts <- or1.pts
                        or1.pts <- tmp
                        Clip.fixOutRecPts or1
                        Clip.fixOutRecPts or2New
                        or2New.owner <- or1
                    elif path1InsidePath2(or2New.pts, or1.pts) then
                        or2New.owner <- or1
                    else
                        or2New.owner <- or1.owner

                    if isNull' or1.splits then
                        or1.splits <- ResizeArray<int>()
                    or1.splits.Add(or2New.idx)
                else
                    or2New.owner <- or1
            else
                or2.pts <- null'()
                if usingPolytree then
                    Clip.setOwner(or2, or1)
                    Clip.moveSplits(or2, or1)
                else
                    or2.owner <- or1

    // ---- Top of scanbeam ----

    let doMaxima (ae: ActiveEdge) : ActiveEdge =
        let prevE = ae.prevInAEL
        let mutable nextE = ae.nextInAEL

        if Clip.isOpenEnd ae then
            if Clip.isHotEdge ae then
                addOutPt(ae, ae.topX, ae.topY, ae.topZ) |> ignore
            if Clip.isHorizontal ae then
                nextE
            else
                if Clip.isHotEdge ae then
                    if Clip.isFront ae then
                        ae.outrec.frontEdge <- null'()
                    else
                        ae.outrec.backEdge <- null'()
                    ae.outrec <- null'()
                deleteFromAEL ae
                nextE
        else
            let maxPair = Clip.getMaximaPair ae
            if isNull' maxPair then
                nextE
            else
                if isJoined ae then
                    split(ae, ae.topX, ae.topY, ae.topZ)
                if isJoined maxPair then
                    split(maxPair, maxPair.topX, maxPair.topY, maxPair.topZ)

                // only non-horizontal maxima here.
                // process any edges between maxima pair ...
                while nextE =!= maxPair do
                    intersectEdges(ae, nextE, ae.topX, ae.topY, ae.topZ)
                    swapPositionsInAEL(ae, nextE)
                    nextE <- ae.nextInAEL

                if Clip.isOpen ae then
                    if Clip.isHotEdge ae then
                        addLocalMaxPoly(ae, maxPair, ae.topX, ae.topY, ae.topZ) |> ignore
                    deleteFromAEL maxPair
                    deleteFromAEL ae
                    if isNotNull prevE then
                        prevE.nextInAEL
                    else
                        actives
                else
                    if Clip.isHotEdge ae then
                        addLocalMaxPoly(ae, maxPair, ae.topX, ae.topY, ae.topZ) |> ignore
                    deleteFromAEL ae
                    deleteFromAEL maxPair
                    if isNotNull prevE then
                        prevE.nextInAEL
                    else
                        actives

    let doTopOfScanbeam (y: float) : unit =
        sel <- null'()
        let mutable ae = actives
        while isNotNull ae do
            if ae.topY = y then
                ae.curX <- ae.topX
                if Clip.isMaximaA ae then
                    ae <- doMaxima ae
                else
                    if Clip.isHotEdge ae then
                        addOutPt(ae, ae.topX, ae.topY, ae.topZ) |> ignore
                    updateEdgeIntoAEL ae
                    if Clip.isHorizontal ae then
                        pushHorz ae
                    ae <- ae.nextInAEL
            else
                ae.curX <- Clip.topX(ae, y)
                ae <- ae.nextInAEL

    // ---- Main execute loop ----

    let executeInternal (ct: ClipType, fr: FillRule) : unit =
        if ct = ClipType.NoClip then
            ()
        else
            Clip.state.openPathsEnabled <- hasOpenPaths
            fillrule <- fr
            cliptype <- ct
            reset()

            let mutable y = popScanline()
            if Double.IsNaN y then
                ()
            else
                let mutable running = true
                while running && succeeded do
                    insertLocalMinimaIntoAEL y
                    let mutable ae = popHorz()
                    while isNotNull ae do
                        doHorizontal ae
                        ae <- popHorz()
                    if horzSegList.Count > 0 then
                        convertHorzSegsToJoins()
                        horzSegList|> Rarr.clear
                    currentBotY <- y
                    let nextY = popScanline()

                    if Double.IsNaN nextY then
                        running <- false
                    else
                        y <- nextY
                        doIntersections y
                        doTopOfScanbeam y
                        ae <- popHorz()
                        while isNotNull ae do
                            doHorizontal ae
                            ae <- popHorz()

                if succeeded then
                    processHorzJoins()

    // ---- Solution post-processing ----

    let doSplitOp (outrec: OutRec, splitOp: OutPt) : unit =
        // splitOp.prev <=> splitOp &&
        // splitOp.next <=> splitOp.next.next are intersecting
        let prevOp = splitOp.prev
        let nextNextOp = splitOp.next.next
        outrec.pts <- prevOp

        // doSplitOp is only reached when segments are known to intersect,
        // so the result is never null here.

        // this sets the tempX , tempY and tempZ fields on the module
        Clip.getLineIntersectPtInState(prevOp, splitOp, splitOp.next, nextNextOp)


        // new logic, setz only if OutPt is actually added to the path, below after OutPt.create (..) ,not here
        // match zCallback with
        // | Some zcallback ->
        //     // tempZ <- callback(prevOp.pt, splitOp.pt, splitOp.next.pt, nextNextOp.pt, tempX, tempY, tempZ)
        //     tempZ <- zcallback(outrec.backEdge, outrec.frontEdge , tempX, tempY, tempZ)
        // | None -> ()

        let doubleArea1 = Clip.areaOutPt prevOp
        let absDoubleArea1 = Math.Abs(doubleArea1)


        // TODO: the 4.0 threshold is somewhat arbitrary and doesn't match C#,
        // compare with ref source


        if absDoubleArea1 < 4.0 then
            outrec.pts <- null'()
        else
            let tempX = Clip.state.tempX
            let tempY = Clip.state.tempY
            let tempZ = Clip.state.tempZ
            let doubleArea2 = Clip.areaTriangle (tempX, tempY, splitOp.x, splitOp.y, splitOp.next.x, splitOp.next.y)
            let absDoubleArea2 = Math.Abs(doubleArea2)

            // de-link splitOp and splitOp.next from the path
            // while inserting the intersection point
            if Clip.xyEqual(tempX, tempY, prevOp.x, prevOp.y) || Clip.xyEqual(tempX, tempY, nextNextOp.x, nextNextOp.y) then
                nextNextOp.prev <- prevOp
                prevOp.next <- nextNextOp
            else
                let newOp2 = OutPt.create (tempX, tempY, tempZ, outrec)
                newOp2.prev <- prevOp
                newOp2.next <- nextNextOp
                nextNextOp.prev <- newOp2
                prevOp.next <- newOp2
                // new logic, setz only if OutPt is actually added to the path,
                setZ(outrec.backEdge, outrec.frontEdge, newOp2)

            // nb: area1 is the path's area *before* splitting, whereas area2 is
            // the area of the triangle containing splitOp & splitOp.next.
            // So the only way for these areas to have the same sign is if
            // the split triangle is larger than the path containing prevOp or
            // if there's more than one self=intersection.
            if not (absDoubleArea2 > 2.0)
                ||(not (absDoubleArea2 > absDoubleArea1) && ((doubleArea2 > 0.0) <> (doubleArea1 > 0.0))) then
                    ()
            else
                let newOutRec = newOutRec()
                newOutRec.owner <- outrec.owner
                splitOp.outrec <- newOutRec
                splitOp.next.outrec <- newOutRec

                let newOp = OutPt.create (tempX, tempY, tempZ, newOutRec)
                newOp.prev <- splitOp.next
                newOp.next <- splitOp
                newOutRec.pts <- newOp
                splitOp.prev <- newOp
                splitOp.next.next <- newOp

                if usingPolytree then
                    if path1InsidePath2(prevOp, newOp) then
                        if isNull' newOutRec.splits then
                            newOutRec.splits <- ResizeArray<int>()
                        newOutRec.splits.Add(outrec.idx)
                    else
                        if isNull' outrec.splits then
                            outrec.splits <- ResizeArray<int>()
                        outrec.splits.Add(newOutRec.idx)

                // new logic, setz only if OutPt is actually added to the path,
                setZ(outrec.backEdge, outrec.frontEdge, newOp)

    let fixSelfIntersects (outrec: OutRec) : unit =
        let mutable op2 = outrec.pts
        if op2.prev === op2.next.next then
            ()
        else
            let mutable loopOn = true
            while loopOn do
                if isNotNull op2.next
                    && isNotNull op2.next.next
                    && Clip.boundingBoxesOverlap (op2.prev.x, op2.prev.y, op2.x, op2.y, op2.next.x, op2.next.y, op2.next.next.x, op2.next.next.y)
                    && Geo.segsIntersectNotInclusive (op2.prev.x, op2.prev.y, op2.x, op2.y, op2.next.x, op2.next.y, op2.next.next.x, op2.next.next.y) then
                        if op2 === outrec.pts || op2.next === outrec.pts then
                            outrec.pts <- outrec.pts.prev
                        doSplitOp(outrec, op2)
                        if isNull' outrec.pts then
                            loopOn <- false
                        else
                            op2 <- outrec.pts
                            if op2.prev === op2.next.next then
                                loopOn <- false
                else
                    op2 <- op2.next
                    if op2 === outrec.pts then
                        loopOn <- false

    let cleanCollinear (outrecIn: OutRec) : unit =
        let outrec = Clip.getRealOutRec outrecIn
        if isNull' outrec || outrec.isOpen then
            ()
        elif not (Clip.isValidClosedPath outrec.pts) then
            outrec.pts <- null'()
        else
            let mutable startOp = outrec.pts
            let mutable op2: OutPt = startOp
            let mutable loopOn = true
            let mutable invalidated = false
            while loopOn && not invalidated do
                if  isNotNull op2
                    && Geo.isCollinear (op2.prev.x, op2.prev.y, op2.x, op2.y, op2.next.x, op2.next.y)
                    && ( Clip.xyEqual(op2.x, op2.y, op2.prev.x, op2.prev.y)
                         || Clip.xyEqual(op2.x, op2.y, op2.next.x, op2.next.y)
                         || not preserveCollinear
                         || Geo.dotProductSign (op2.prev.x, op2.prev.y, op2.x, op2.y, op2.next.x, op2.next.y) < 0
                    ) then
                                if op2 === outrec.pts then
                                    outrec.pts <- op2.prev
                                op2 <- Clip.disposeOutPt op2
                                if not (Clip.isValidClosedPath op2) then
                                    outrec.pts <- null'()
                                    invalidated <- true
                                else
                                    startOp <- op2
                elif isNull' op2 then
                    loopOn <- false
                else
                    op2 <- op2.next
                    if op2 === startOp then
                        loopOn <- false
            if not invalidated && isNotNull outrec.pts then
                fixSelfIntersects outrec



    let checkBounds (outrec: OutRec) : bool =
        if isNull' outrec.pts then
            false
        elif not (Clip.isEmptyRect outrec) then
            true
        else
            cleanCollinear outrec
            if isNull' outrec.pts || not (Clip.buildPath(outrec.pts, reverseSolution, false, outrec.path)) then
                false
            else
                Clip.getBounds(outrec)
                true


    let rec checkSplitOwner (outrec: OutRec, splits: ResizeArray<int>) : bool =
        let mutable result = false
        let mutable i = 0
        while (not result) && i < splits.Count do
            let mutable split = outrecList[splits[i]]
            if isNull' split.pts && isNotNull split.splits &&
               checkSplitOwner(outrec, split.splits) then
                result <- true
            else
                split <- Clip.getRealOutRec split
                if isNull' split ||
                   split === outrec ||
                   split.recursiveSplit === outrec then
                    ()
                else
                    split.recursiveSplit <- outrec

                    if isNotNull split.splits && checkSplitOwner(outrec, split.splits) then
                        result <- true
                    elif not (checkBounds split) ||
                         not (Clip.containsRect(split, outrec)) ||
                         not (path1InsidePath2(outrec.pts, split.pts)) then
                        ()
                    else
                        if not (Clip.isValidOwner(outrec, split)) then
                            split.owner <- outrec.owner
                        outrec.owner <- split
                        result <- true
            i <- i + 1
        result

    let buildPaths (solutionClosed: Paths64, solutionOpen: Paths64) : bool =
        let outrecList = outrecList // avoiding access via 'this' in JS
        solutionClosed|> Rarr.clear
        if isNotNull solutionOpen then
            solutionOpen|> Rarr.clear

        // outrecList.length is not static here because
        // CleanCollinear can indirectly add additional OutRec
        let mutable i = 0
        while i < outrecList.Count do
            let outrec = outrecList[i]
            i <- i + 1
            if isNotNull outrec.pts then
                let path = Path64()
                if outrec.isOpen then
                    if Clip.buildPath(outrec.pts, reverseSolution, true, path) then
                        if isNotNull solutionOpen then
                            solutionOpen.Add(path)
                else
                    cleanCollinear outrec
                    // closed paths should always return a Positive orientation
                    // except when ReverseSolution == true
                    if Clip.buildPath(outrec.pts, reverseSolution, false, path) then
                        solutionClosed.Add(path)
        true

    let rec recursiveCheckOwners (outrec: OutRec, polypath: PolyPath64) : unit =
        if isNotNull outrec.polypath || Clip.isEmptyRect outrec then
            ()
        else
            let mutable breakLoop = false
            while (not breakLoop) && isNotNull outrec.owner do
                if isNotNull outrec.owner.splits && checkSplitOwner(outrec, outrec.owner.splits) then
                    breakLoop <- true
                elif isNotNull outrec.owner.pts
                    && checkBounds outrec.owner
                    && Clip.containsRect(outrec.owner, outrec)
                    && path1InsidePath2(outrec.pts, outrec.owner.pts) then
                        breakLoop <- true
                else
                    outrec.owner <- outrec.owner.owner

            if isNotNull outrec.owner then
                if isNull' outrec.owner.polypath then
                    recursiveCheckOwners(outrec.owner, polypath)
                outrec.polypath <- outrec.owner.polypath.AddChild(outrec.path)
            else
                outrec.polypath <- polypath.AddChild(outrec.path)

    let buildTree (polytree: PolyPath64, solutionOpen: Paths64) : unit =
        let outrecList = outrecList // avoiding access via 'this' in JS
        polytree.ClearContent()
        if isNotNull solutionOpen then
            solutionOpen|> Rarr.clear

        // outrecList.length is not static here because
        // checkBounds below can indirectly add additional
        // OutRec (via FixOutRecPts & CleanCollinear)
        let mutable i = 0
        while i < outrecList.Count do
            let outrec = outrecList[i]
            i <- i + 1
            if isNotNull outrec.pts then
                if outrec.isOpen then
                    let openPath = Path64()
                    if Clip.buildPath(outrec.pts, reverseSolution, true, openPath) then
                        if isNotNull solutionOpen then
                            solutionOpen.Add(openPath)
                else
                    if checkBounds outrec then
                        recursiveCheckOwners(outrec, polytree)

    //#endregion
    //#region Public interface

    member _.PreserveCollinear
        with get() : bool = preserveCollinear
        and set(v: bool) : unit = preserveCollinear <- v

    member _.ReverseSolution
        with get() : bool = reverseSolution
        and set(v: bool) : unit = reverseSolution <- v


    /// Called at each intersection to allow custom Z value computation.
    /// (This Z member is for user defined data and has nothing to do with 3D clipping.)
    ///
    /// While most vertices in clipping solutions will correspond to input (subject and clip) vertices,
    /// there will also be new vertices wherever these segments intersect.
    /// This callback facilitates assigning user-defined Z values at these intersections.
    /// To aid the user in determining appropriate Z values,
    /// the function receives the vertices at both ends of the intersecting segments (ie four vertices).

    /// Receives the adjacent active edges and the computed intersection ordinates, and returns the new Z value.
    /// NULL warning: maybe ActiveEdge may be null when the callback is invoked from doSplitOp.
    member _.ZCallback
        with get() : ZCallback64 option = zCallback
        and set(v: ZCallback64 option) : unit = zCallback <- v



    member _.ClearAll() : unit =
        clearSolutionOnly()
        minimaList|> Rarr.clear
        vertexList|> Rarr.clear
        currentLocMin <- 0
        isSortedMinimaList <- false
        hasOpenPaths <- false


    member _.AddPaths(paths: Paths64, pathType: PathType, [<OPT; DEF(false)>] isOpen: bool) : unit =
        if isOpen then
            hasOpenPaths <- true
        isSortedMinimaList <- false
        Clip.addPathsToVertexList(paths, pathType, isOpen, minimaList, vertexList)

    member this.AddPath(path: Path64, pathType: PathType, [<OPT; DEF(false)>] isOpen: bool) : unit =
        let tmp = Paths64()
        tmp.Add(path)
        this.AddPaths(tmp, pathType, isOpen)

    member this.AddSubject(paths: Paths64) : unit =
        this.AddPaths(paths, PathType.Subject, false)

    member this.AddOpenSubject(paths: Paths64) : unit =
        this.AddPaths(paths, PathType.Subject, true)

    member this.AddClip(paths: Paths64) : unit =
        this.AddPaths(paths, PathType.Clip, false)

    /// Fills the provided solutionClosed Paths64 with the closed paths in the solution, and optionally solutionOpen with the open paths if solutionOpen paths are provided and not null.
    /// Returns a boolean indicating success or failure of the operation, and
    member _.Execute(clipType: ClipType, fillRule: FillRule, solutionClosed: Paths64, ?solutionOpen: Paths64) : bool = // using DefaultArg(null) would fail in Fable TS build
        let solutionOpen = defaultArg solutionOpen null
        solutionClosed|> Rarr.clear
        if isNotNull solutionOpen then
            solutionOpen|> Rarr.clear
        try
            try
                executeInternal(clipType, fillRule)
                buildPaths(solutionClosed, solutionOpen) |> ignore
            with _ ->
                succeeded <- false
        finally
            clearSolutionOnly()
        succeeded

    /// Fills the provided PolyTree64 with the result of the operation and optionally openPaths with the open paths if openPaths paths are provided and not null.
    /// Returns a boolean indicating success or failure,
    member _.Execute(clipType: ClipType, fillRule: FillRule, polytree: PolyTree64, ?openPaths: Paths64) : bool = // using DefaultArg(null) would fail in Fable TS build
        let openPaths = defaultArg openPaths null
        polytree.ClearContent()
        if isNotNull openPaths then
            openPaths|> Rarr.clear
        usingPolytree <- true
        try
            try
                executeInternal(clipType, fillRule)
                buildTree(polytree, openPaths)
            with _ ->
                succeeded <- false
        finally
            clearSolutionOnly()
        succeeded

