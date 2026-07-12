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
open Klip.KlipInternalTypes



// #region module Eng

// Internal helper functions that were `private static` on ClipperBase in TS.
module internal Eng =

    /// Process-wide default for Clipper64.ScanlineArrayThreshold, read once when a Clipper64
    /// is constructed; set via Klipper.setDefaultScanlineArrayThreshold. Performance tuning
    /// only - any value produces identical clipping results. (Fable compiles this mutable to a
    /// getter/setter atom, so reads and writes from other modules work in JS too.)
    let mutable defaultScanlineArrayThreshold = 64

    /// Evaluates `from + param * len`, the only place where new x and y coordinates are created.
    let inline evalAt (from:float) (param: float) (len: float): float =
        // Clipper2 C#:
        // Historically this truncated or rounded to the integer grid;
        // avoid using constructor (and rounding too) as they affect performance //664
        // seems to apply for C++ only ? https://github.com/AngusJohnson/Clipper2/pull/657#issuecomment-1732206640
        from + param * len // no more rounding (to int64) here


    // GetLineIntersectPt - returns the intersection point if non-parallel, or null.
    // The point will be constrained to seg1. However, it's possible that the point
    // won't be inside seg2, even when it hasn't been constrained (ie inside seg1).
    let getLineIntersectPt(ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>, topY: float) : IntersectNode<'Z> =
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
            z = Null.DEFZ()
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
                {
                x = evalAt ae1.botX t dx1
                y = evalAt ae1.botY t dy1
                z = Null.DEFZ()
                edge1 = ae1
                edge2 = ae2
                }

    // actually should be called setClosestPtOnSegment, but kept the original name for consistency
    let getClosestPtOnSegment (xn: IntersectNode<'Z>, seg1X: float, seg1Y: float, seg2X: float, seg2Y: float): unit =
        if seg1X = seg2X && seg1Y = seg2Y then
            xn.x <- seg1X
            xn.y <- seg1Y
        else
            let dx = seg2X - seg1X
            let dy = seg2Y - seg1Y
            let q = ((xn.x - seg1X) * dx + (xn.y - seg1Y) * dy) / (dx * dx + dy * dy)
            if q <= 0.0 then
                xn.x <- seg1X
                xn.y <- seg1Y
            elif q >= 1.0 then
                xn.x <- seg2X
                xn.y <- seg2Y
            else
                xn.x <- evalAt seg1X q dx
                xn.y <- evalAt seg1Y q dy

    // NB: LocalMinima is a struct, so there is no null check here (boxing one would always be non-null).
    let inline localMinimaEqual (localMinima: LocalMinima<'Z>, other: LocalMinima<'Z>) : bool =
        localMinima.vertex === other.vertex

    let inline isOdd (v: int) : bool =
        (v &&& 1) <> 0

    let inline isHotEdge (ae: ActiveEdge<'Z>) : bool =
        isNotNull ae.outrec

    let inline isOpenEndVertex (v: Vertex<'Z>) : bool =
        v.flags &&& (VertexFlags.OpenStart ||| VertexFlags.OpenEnd) <> VertexFlags.None

    let getPrevHotEdge (openPathsEnabled: bool, ae: ActiveEdge<'Z>) : ActiveEdge<'Z> =
        let mutable prev = ae.prevInAEL
        if not openPathsEnabled then
            // Fast path: when open paths are disabled, avoid calling isOpen() in the loop.
            while isNotNull prev && not (isHotEdge prev) do
                prev <- prev.prevInAEL
            prev
        else
            while isNotNull prev && (prev.localMin.isOpen || not (isHotEdge prev)) do
                prev <- prev.prevInAEL
            prev

    let inline isFront (ae: ActiveEdge<'Z>) : bool =
        ae === ae.outrec.frontEdge

    /// Scale-relative angle tolerance for treating an edge as horizontal. An edge counts as
    /// horizontal when |Δy| <= horzAngleTol * |Δx| AND |Δy| <= coordEqTol, i.e. its slope from
    /// horizontal is within this (dimensionless, sin θ-like) tolerance and its two endpoint Ys
    /// are point-coincident. Unrounded inputs can leave a shared near-horizontal edge a hair
    /// off exact (e.g. a top edge at 37 vs 37.0000000001), which the former exact
    /// `topY = botY` test missed, landing the two ends on distinct scanlines and sealing open
    /// notches into phantom holes.
    /// The coordEqTol cap is essential: a long shallow edge can satisfy the angle test with an
    /// endpoint-Y difference far above the point-coincidence tolerance. Its endpoints are then
    /// genuinely distinct points, and collapsing them onto one scanline makes doHorizontal
    /// consume the edge a scanbeam before the far-end scanline, stranding another contour's
    /// seam edge arriving there (the seam never merges). The cap also keeps classification
    /// consistent with the horizontal-segment machinery, whose output-point run walks use
    /// point-coincidence (isEqualTol) on Y.
    /// Both tolerances are carried by the caller (the per-instance
    /// `Clipper64.HorizontalAngleTolerance` / `CoordEqTolerance`) rather than a module-global,
    /// so two clips can use different tolerances without interfering.
    let inline isHorizontalCoords (horzAngleTol: float, coordEqTol: float, botX: float, botY: float, topX: float, topY: float) : bool =
        let dy = abs (topY - botY)
        dy <= horzAngleTol * abs (topX - botX) && dy <= coordEqTol

    let inline getDx (horzAngleTol: float, coordEqTol: float, pt1X: float, pt1Y: float, pt2X: float, pt2Y: float) : float =
        let dy = pt2Y - pt1Y
        if not (isHorizontalCoords (horzAngleTol, coordEqTol, pt1X, pt1Y, pt2X, pt2Y)) then
            (pt2X - pt1X) / dy
        elif pt2X > pt1X then
            Double.NegativeInfinity
        else
            Double.PositiveInfinity

    let inline setDx (horzAngleTol: float, coordEqTol: float, ae: ActiveEdge<'Z>) : unit =
        ae.dx <- getDx (horzAngleTol, coordEqTol, ae.botX, ae.botY, ae.topX, ae.topY)

    let inline topX (ae: ActiveEdge<'Z>, currentY: float) : float =
        if currentY = ae.topY || ae.topX = ae.botX then
            ae.topX
        elif currentY = ae.botY then
            ae.botX
        else
            let x = evalAt ae.botX ae.dx (currentY - ae.botY)
            if Double.IsInfinity x then
                // Only a horizontal-classified edge (dx = ±infinity) can overflow the
                // interpolation to ±infinity (botX is finite, and currentY = botY is
                // handled above). Such an edge is normally consumed within its own
                // scanbeam and never queried here, but with unrounded coordinates one
                // can be parked in the AEL awaiting its maxima pair (see doHorizontal).
                // An infinite curX would corrupt the SEL ordering; a parked edge rests
                // at its top vertex instead. Testing the result rather than dx keeps
                // the guard off the path every sloped edge pays at every scanbeam.
                ae.topX
            else
                x


    let inline isHorizontal (horzAngleTol: float, coordEqTol: float, ae: ActiveEdge<'Z>) : bool =
        isHorizontalCoords (horzAngleTol, coordEqTol, ae.botX, ae.botY, ae.topX, ae.topY)

    let inline isHeadingRightHorz (ae: ActiveEdge<'Z>) : bool =
        ae.dx = Double.NegativeInfinity

    let inline isHeadingLeftHorz (ae: ActiveEdge<'Z>) : bool =
        ae.dx = Double.PositiveInfinity

    let inline isSamePolyType (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>) : bool =
        ae1.localMin.pathType = ae2.localMin.pathType

    let inline nextVertex (ae: ActiveEdge<'Z>) : Vertex<'Z> =
        if ae.windDx > 0 then
            ae.vertexTop.next
        else
            ae.vertexTop.prev

    let inline prevPrevVertex (ae: ActiveEdge<'Z>) : Vertex<'Z> =
        if ae.windDx > 0 then
            ae.vertexTop.prev.prev
        else
            ae.vertexTop.next.next

    let inline isMaximaV (v: Vertex<'Z>) : bool =
        v.flags &&& VertexFlags.LocalMax <> VertexFlags.None

    let inline isMaximaA (ae: ActiveEdge<'Z>) : bool =
        isMaximaV ae.vertexTop

    let getMaximaPair (ae: ActiveEdge<'Z>) : ActiveEdge<'Z> =
        let mutable ae2 = ae.nextInAEL
        let mutable result: ActiveEdge<'Z> = null'()
        let mutable loopOn = true
        while loopOn && isNotNull ae2 do
            if ae2.vertexTop === ae.vertexTop then
                result <- ae2
                loopOn <- false
            else
                ae2 <- ae2.nextInAEL
        result

    // The "really close" window is an absolute distance inherited from integer-grid
    // Clipper2 (2 grid units). It is carried by the caller (the per-instance
    // `Clipper64.SmallTriangleTolerance`, default 2.0) so it can be scaled to the
    // coordinate magnitude of the input, like the other absolute tolerances.
    let inline ptsReallyClose (closeTol: float, pt1X: float, pt1Y: float, pt2X: float, pt2Y: float) : bool =
        Math.Abs(pt1X - pt2X) < closeTol && Math.Abs(pt1Y - pt2Y) < closeTol

    let isVerySmallTriangle (closeTol: float, op: OutPt<'Z>) : bool =
        op.next.next === op.prev &&
        (ptsReallyClose (closeTol, op.prev.x, op.prev.y, op.next.x, op.next.y) ||
         ptsReallyClose (closeTol, op.x, op.y, op.next.x, op.next.y) ||
         ptsReallyClose (closeTol, op.x, op.y, op.prev.x, op.prev.y))

    /// Signed double-area of a closed OutPt<'Z> ring.
    let areaOutPt<'Z> (op: OutPt<'Z>) : float =
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
    /// Early-exits on the first separating axis found instead of computing all
    /// eight min/max values up front.
    let inline boundingBoxesOverlap (p1X: float, p1Y: float, p2X: float, p2Y: float, p3X: float, p3Y: float, p4X: float, p4Y: float) : bool =
        let min1x = if p1X < p2X then p1X else p2X
        let max2x = if p3X > p4X then p3X else p4X
        if max2x < min1x then
            false
        else
            let max1x = if p1X > p2X then p1X else p2X
            let min2x = if p3X < p4X then p3X else p4X
            if max1x < min2x then
                false
            else
                let min1y = if p1Y < p2Y then p1Y else p2Y
                let max2y = if p3Y > p4Y then p3Y else p4Y
                if max2y < min1y then
                    false
                else
                    let max1y = if p1Y > p2Y then p1Y else p2Y
                    let min2y = if p3Y < p4Y then p3Y else p4Y
                    max1y >= min2y


    /// Fills the solution Path64 from OutPt doubly linked list.
    /// Returns false if the resulting path is invalid (eg too few points, or a very small triangle in closed paths).
    let buildPath (op: OutPt<'Z>, reverse: bool, isOpen: bool, path: Path64<'Z>, coordEqTol: float, smallTriangleTol: float) : bool =
        if isNull' op || op === op.next || (not isOpen && op.next === op.prev) then
            false
        else
            path.Clear()
            let mutable opL = op
            let mutable lastX = 0.0
            let mutable lastY = 0.0
            let mutable op2: OutPt<'Z> = null'()
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
                if Geo.isNotEqualWithin coordEqTol op2.x lastX || Geo.isNotEqualWithin coordEqTol op2.y lastY then
                    lastX <- op2.x
                    lastY <- op2.y
                    path.Add(lastX, lastY, op2.z)
                if reverse then
                    op2 <- op2.prev
                else
                    op2 <- op2.next

            path.PointCount <> 3 || isOpen || not (isVerySmallTriangle (smallTriangleTol, op2))


    let inline containsRect (rect:OutRec<'Z>, rec_:OutRec<'Z>) : bool =
        rec_.boundsLeft >= rect.boundsLeft && rec_.boundsRight  <= rect.boundsRight &&
        rec_.boundsTop  >= rect.boundsTop  && rec_.boundsBottom <= rect.boundsBottom

    /// actually this does set bounds
    let getBounds (outRec:OutRec<'Z>) : unit =
        if outRec.path.PointCount > 0 then
            let coords = outRec.path.XYs
            let mutable left = Double.MaxValue
            let mutable top = Double.MaxValue
            let mutable right = Double.MinValue
            let mutable bottom = Double.MinValue
            for i = 0 to outRec.path.PointCount - 1 do
                let coord = i * 2
                let x = Rarr.getIdx coord coords
                let y = Rarr.getIdx (coord + 1) coords
                if x < left   then left <- x
                if x > right  then right <- x
                if y < top    then top <- y
                if y > bottom then bottom <- y
            outRec.boundsLeft <- left
            outRec.boundsTop <- top
            outRec.boundsRight <- right
            outRec.boundsBottom <- bottom

    let inline isEmptyRect (outRec:OutRec<'Z>) : bool =
        outRec.boundsBottom <= outRec.boundsTop || outRec.boundsRight <= outRec.boundsLeft


    /// Perpendicular distance from pt to line (line1,line2) squared > distance tolerance squared?
    let distFromLineSqrdGreaterThanTolerance ( distanceToleranceSqrd:float, ptX:float, ptY:float, line1X:float, line1Y:float, line2X:float, line2Y:float) : bool =
        let a = ptX - line1X
        let b = ptY - line1Y
        let c = line2X - line1X
        let d = line2Y - line1Y
        if c = 0.0 && d = 0.0 then
            false
        else
            let cross = a * d - c * b
            (cross * cross) / (c*c + d*d) > distanceToleranceSqrd // this used to be just 0.25 in the past in Original Clipper2 in CheckJoinLeft function

    let inline addToLocalMinimaList (vert: Vertex<'Z>, pathType: PathType, isOpen: bool,  minimaList: ResizeArray<LocalMinima<'Z>>) : unit =
        // make sure the vertex is added only once ...
        if (vert.flags &&& VertexFlags.LocalMin) <> VertexFlags.None then
            ()
        else
            vert.flags <- vert.flags ||| VertexFlags.LocalMin
            minimaList.Add  {vertex = vert; pathType = pathType; isOpen = isOpen}



    let intersectListSort: Comparison<IntersectNode<'Z>> =
        Comparison<IntersectNode<'Z>>(fun (a:IntersectNode<'Z>) (b:IntersectNode<'Z>) ->
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


    let horzSegSort: Comparison<HorzSegment<'Z>> =
        Comparison<HorzSegment<'Z>>(fun (hs1: HorzSegment<'Z>) (hs2: HorzSegment<'Z>) ->
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

    let minimaListCmp : Comparison<LocalMinima<'Z>> =
        Comparison<LocalMinima<'Z>>(fun (a:LocalMinima<'Z>) (b:LocalMinima<'Z>) ->
            let ya = a.vertex.y
            let yb = b.vertex.y
            if ya > yb then
                -1
            elif ya < yb then
                1
            else
                0
        )


    let inline edgesAdjacentInAEL (inode: IntersectNode<'Z>) : bool =
        inode.edge1.nextInAEL === inode.edge2 ||
        inode.edge1.prevInAEL === inode.edge2

    // ---- OutPt<'Z> helpers ----

    let duplicateOp (op: OutPt<'Z>, insertAfter: bool) : OutPt<'Z> =
        let result = OutPt<'Z>.create (op.x, op.y, op.z, op.outrec)
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

    let inline disposeOutPt<'Z> (op: OutPt<'Z>) : OutPt<'Z> =
        let result =
            if op.next === op then
                null'()
            else
                op.next
        op.prev.next <- op.next
        op.next.prev <- op.prev
        result

    let inline isValidClosedPath (smallTriangleTol: float, op: OutPt<'Z>) : bool =
        isNotNull op && op.next =!= op &&
        (op.next =!= op.prev || not (isVerySmallTriangle (smallTriangleTol, op)))

    let inline outrecIsAscending (hotEdge: ActiveEdge<'Z>) : bool =
        hotEdge === hotEdge.outrec.frontEdge

    // ---- OutRec<'Z> helpers ----

    let getRealOutRec (outRec: OutRec<'Z>) : OutRec<'Z> =
        let mutable o = outRec
        while isNotNull o && isNull' o.pts do
            o <- o.owner
        o

    let inline setSides (outrec: OutRec<'Z>, startEdge: ActiveEdge<'Z>, endEdge: ActiveEdge<'Z>) : unit =
        outrec.frontEdge <- startEdge
        outrec.backEdge <- endEdge

    let inline swapFrontBackSides (outrec: OutRec<'Z>) : unit =
        let ae2 = outrec.frontEdge
        outrec.frontEdge <- outrec.backEdge
        outrec.backEdge <- ae2
        outrec.pts <- outrec.pts.next

    let setOwner (outrec: OutRec<'Z>, newOwner: OutRec<'Z>) : unit =
        let mutable owner = newOwner
        while isNotNull owner.owner && isNull' owner.owner.pts do
            owner.owner <- owner.owner.owner
        // Make sure outrec isn't an ancestor of newOwner
        let mutable tmp: OutRec<'Z> = owner
        let mutable loopOn = true
        while loopOn && isNotNull tmp && tmp =!= outrec do
            tmp <- tmp.owner
            if isNull' tmp then
                loopOn <- false
        if isNotNull tmp then
            owner.owner <- outrec.owner
        outrec.owner <- owner

    let inline moveSplits (fromOr: OutRec<'Z>, toOr: OutRec<'Z>) : unit =
        if isNull' fromOr.splits then
            ()
        else
            if isNull' toOr.splits then
                toOr.splits <- ResizeArray<int>()
            for i=0 to fromOr.splits |> Rarr.lastIdx do
                let idx = Rarr.getIdx i fromOr.splits
                if idx <> toOr.idx then
                    toOr.splits.Add(idx)
            fromOr.splits <- null'()

    let inline fixOutRecPts (outrec: OutRec<'Z>) : unit =
        let mutable op = outrec.pts
        let mutable loopOn = true
        while loopOn do
            op.outrec <- outrec
            op <- op.next
            if op === outrec.pts then
                loopOn <- false

    let inline uncoupleOutRec (ae: ActiveEdge<'Z>) : unit =
        let outrec = ae.outrec
        if isNull' outrec then
            ()
        else
            outrec.frontEdge.outrec <- null'()
            outrec.backEdge.outrec <- null'()
            outrec.frontEdge <- null'()
            outrec.backEdge <- null'()


    let inline isValidOwner (outRec: OutRec<'Z>, testOwnerIn: OutRec<'Z>) : bool =
        let mutable testOwner = testOwnerIn
        while isNotNull testOwner && testOwner =!= outRec do
            testOwner <- testOwner.owner
        isNull' testOwner


    let inline getLastOp (hotEdge: ActiveEdge<'Z>) : OutPt<'Z> =
        let outrec = hotEdge.outrec
        if hotEdge === outrec.frontEdge then
            outrec.pts
        else
            outrec.pts.next


    // ---- Horizontal processing ----

    let trimHorz (horzAngleTol: float, coordEqTol: float, horzEdge: ActiveEdge<'Z>, preserveColinearFlag: bool) : unit =
        let mutable wasTrimmed = false
        let mutable pt = nextVertex horzEdge
        let mutable loopOn = true
        while loopOn && pt.y = horzEdge.topY do
            // always trim 180 deg. spikes (in closed paths)
            // but otherwise break if preserveColinear = true
            if preserveColinearFlag && ((pt.x < horzEdge.topX) <> (horzEdge.botX < horzEdge.topX)) then
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
            setDx (horzAngleTol, coordEqTol, horzEdge)

    let getCurrYMaximaVertex (ae: ActiveEdge<'Z>) : Vertex<'Z> =
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


    let getCurrYMaximaVertexOpen (ae: ActiveEdge<'Z>) : Vertex<'Z> =
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


    let pointInOpPolygon (coordEqTol: float) (colinTolSqrd: float) (ptX: float) (ptY: float) (op: OutPt<'Z>) : PointInPolygonResult =
        let inline crossProductSign args = Geo.crossProductSign colinTolSqrd args
        if op === op.next || op.prev === op.next then
            PointInPolygonResult.IsOutside
        else
            let mutable opL = op
            let mutable op2 = opL
            let mutable loopOn = true
            while loopOn do
                if Geo.isNotEqualWithin coordEqTol opL.y ptY then
                    loopOn <- false
                else
                    opL <- opL.next
                    if opL === op2 then
                        loopOn <- false
            if Geo.isEqualWithin coordEqTol opL.y ptY then
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
                        if Geo.isEqualWithin coordEqTol op2b.y ptY then
                            if Geo.isEqualWithin coordEqTol op2b.x ptX ||(Geo.isEqualWithin coordEqTol op2b.y op2b.prev.y && ((ptX < op2b.prev.x) <> (ptX < op2b.x))) then
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
                                    let d = crossProductSign (op2b.prev.x, op2b.prev.y, op2b.x, op2b.y, ptX, ptY)
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
                    let d = crossProductSign (op2b.prev.x, op2b.prev.y, op2b.x, op2b.y, ptX, ptY)
                    if d = 0 then
                        PointInPolygonResult.IsOn
                    else
                        if (d < 0) = isAbove then
                            value <- 1 - value
                        if value = 0 then
                            PointInPolygonResult.IsOutside
                        else
                            PointInPolygonResult.IsInside

