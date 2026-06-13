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


/// While most vertices in clipping solutions will correspond to input (subject and clip) vertices,
/// there will also be new vertices wherever these segments intersect.
/// This callback facilitates assigning user-defined Z objects at these intersections.
/// To aid the user in determining appropriate values,
/// the function receives the Active Edges at both ends of the intersecting segments.
/// The argument order prioritizes subject vertices over clip vertices.
///
/// It also receives the computed X and Y coordinates of the intersection,
/// and the default assigned adjacent Z object (which may be null).
/// It returns a new Z object.
/// They are called `Z` in Clipper2, but don't confuse them with 3D coordinates.
/// These can be any user-defined objects.
type ZCallback64<'Z> =
    // I am not sure if Active Edge may be null in some  cases, so I will make them option types to be safe.
    option<ActiveEdge<'Z>> * option<ActiveEdge<'Z>> * float * float * 'Z -> 'Z


// #endregion
// #region **Clipper64**

/// Merged Clipper executor for integer (Point64) paths.
/// The TypeScript base/concrete split is collapsed here because this F# port
/// omits the double-precision ClipperD hierarchy.
type Clipper64<'Z>() =

    // these two get filled up while adding Paths:
    let minimaList = ResizeArray<LocalMinima<'Z>>()
    let vertexList = ResizeArray<Vertex<'Z>>() // TODO preallocate because size is known ?


    /// Decides whether pending scanline Ys are stored in scanlineArr or scanlineHeapSet.
    /// Chosen per Execute in sortMinimaResetScanlines from the local-minima count (a proxy for
    /// the number of scanlines); insertScanline flips it to false mid-sweep when the pending
    /// count outgrows scanlineArrayThreshold.
    let mutable useScanlineArray = false
    let scanlineArr = ScanlineArray()       // used while the pending scanline count is small (at most scanlineArrayThreshold)
    let scanlineHeapSet = ScanlineHeapSet() // used when there are more than scanlineArrayThreshold pending scanlines
    // Switch-over size between the two scanline containers; exposed as ScanlineArrayThreshold.
    // Performance tuning only — any value produces identical clipping results.
    let mutable scanlineArrayThreshold = Eng.defaultScanlineArrayThreshold

    let mutable cliptype = ClipType.NoClip
    let mutable fillrule = FillRule.EvenOdd
    let mutable actives: ActiveEdge<'Z> = null'()
    let mutable sel: ActiveEdge<'Z> = null'()
    let intersectList = ResizeArray<IntersectNode<'Z>>()
    let outrecList = ResizeArray<OutRec<'Z>>()
    let horzSegList = ResizeArray<HorzSegment<'Z>>()
    let horzJoinList = ResizeArray<HorzJoin<'Z>>()

    let mutable currentLocMin = 0
    let mutable currentBotY = 0.0
    let mutable isSortedMinimaList = false
    let mutable hasOpenPaths = false
    // Per-instance mirror of `hasOpenPaths`, set before each execute and passed explicitly
    // into the `Clip` open-path predicates (formerly a process-global `Clip.state`, which
    // made concurrent use of two Clipper64 instances unsafe).
    let mutable openPathsEnabled = false
    let mutable usingPolytree = false

    let mutable preserveColinear = true

    // new tolerance properties that Clipper2 didn't have:
    let mutable coordEqTol = 1e-5 // per-instance; exposed as CoordEqTolerance
    let mutable mergeVertexToleranceSqrd = coordEqTol * coordEqTol // defaults to the same value as coordEqTol but can be tuned independently; exposed as MergeVertexTolerance

    let mutable colinTolSqrd = 1e-6  // 1e-3 * 1e-3 //  0.25 in Clipper2 // per-instance (squared); exposed as ColinearityTolerance
    let mutable horzAngleTol = 1e-6 // per-instance; exposed as HorizontalAngleTolerance
    let mutable nearTopYToleranceFactor = 1e-4 // edge-height-relative part of the near-top join guard; tune via NearTopYToleranceFactor
    let mutable nearTopYToleranceCap = 2.0    // absolute ceiling of the near-top join guard; tune via NearTopYToleranceCap
    let mutable smallTriangleTol = 2.0 // per-instance; exposed as SmallTriangleTolerance (2 grid units in integer Clipper2)
    let mutable splitAreaTol = 2.0     // per-instance (area units); exposed as SplitAreaTolerance

    // closed paths should always return a Positive orientation
    // except when ReverseSolution == true
    let mutable reverseSolution = false
    let mutable zCallback: ZCallback64<'Z> option = None

    let mutable tempX : float = 0.0
    let mutable tempY : float = 0.0
    let mutable tempZ : 'Z = Null.DEFZ()

    let mutable hasZValues = false

    // Per-instance coordinate-equality predicates reading this instance's `coordEqTol`
    // (the public `CoordEqTolerance`), used throughout the sweep so two `Clipper64` instances
    // can clip with different tolerances. Coincidence tests in the `Clip`/`Geo` modules take the
    // tolerance as an explicit argument (`Geo.isEqualWithin` / `isNotEqualWithin`); there is no global.
    let isEqualTol (a: float) (b: float) : bool =
        abs (a - b) <= coordEqTol

    let isNotEqualTol (a: float) (b: float) : bool =
        abs (a - b) > coordEqTol

    let xyEqual (ax: float, ay: float, bx: float, by: float) : bool =
        abs (ax - bx) <= coordEqTol &&
        abs (ay - by) <= coordEqTol

    let xyNotEqual (ax: float, ay: float, bx: float, by: float) : bool =
        abs (ax - bx) > coordEqTol ||
        abs (ay - by) > coordEqTol

    // Per-instance horizontal-edge predicates reading this instance's `horzAngleTol`
    // (the public `HorizontalAngleTolerance`); the `Clip` primitives take the tolerance explicitly.
    let isHorizontal (ae: ActiveEdge<'Z>) : bool =
        Eng.isHorizontalCoords (horzAngleTol, ae.botX, ae.botY, ae.topX, ae.topY)


    // Per-instance open-path predicates reading this instance's `openPathsEnabled` flag;
    // the `Clip` primitives take the flag explicitly, like the tolerance arguments.
    let isOpen (ae: ActiveEdge<'Z>) : bool =
        openPathsEnabled &&
        ae.localMin.isOpen

    let isOpenEnd (ae: ActiveEdge<'Z>) : bool =
        openPathsEnabled &&
        ae.localMin.isOpen &&
        Eng.isOpenEndVertex ae.vertexTop

    let getPrevHotEdge (ae: ActiveEdge<'Z>) : ActiveEdge<'Z> =
        let mutable prev = ae.prevInAEL
        if not openPathsEnabled then
            // Fast path: when open paths are disabled, avoid calling isOpen() in the loop.
            while isNotNull prev && not (Eng.isHotEdge prev) do
                prev <- prev.prevInAEL
            prev
        else
            while isNotNull prev && (prev.localMin.isOpen || not (Eng.isHotEdge prev)) do
                prev <- prev.prevInAEL
            prev


    // same as getLineIntersectPt but only called from doSplitOp
    let getLineIntersectPtInState(ln1a: OutPt<'Z>, ln1b: OutPt<'Z>, ln2a: OutPt<'Z>, ln2b: OutPt<'Z>) : unit =
        let dy1 = ln1b.y - ln1a.y
        let dx1 = ln1b.x - ln1a.x
        let dy2 = ln2b.y - ln2a.y
        let dx2 = ln2b.x - ln2a.x
        let det = dy1 * dx2 - dy2 * dx1
        // doSplitOp is only reached when segments are known to intersect,
        // so det is never zero here and can be used as divisor. (see clipper2-ts comments)
        let t = ((ln1a.x - ln2a.x) * dy2 - (ln1a.y - ln2a.y) * dx2) / det
        if t <= 0.0 then
            tempX <- ln1a.x
            tempY <- ln1a.y
            tempZ <- ln1a.z
        elif t >= 1.0 then
            tempX <- ln1b.x
            tempY <- ln1b.y
            tempZ <- ln1b.z
        else
            tempX <- Eng.evalAt ln1a.x t dx1
            tempY <- Eng.evalAt ln1a.y t dy1
            tempZ <- Null.DEFZ()


    // Returns the intersection Z value, possibly updated by the callback.
    let setZ (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>, opt:OutPt<'Z>) : unit =
        match zCallback with
        | None -> ()
        | Some zcallback ->
            let mutable updatedZ = opt.z
            // prioritize subject vertices over clip vertices
            // and pass the subject vertices before clip vertices in the callback
            let intersectX = opt.x
            let intersectY = opt.y
            if ae1.localMin.pathType = PathType.Subject then
                if   xyEqual (intersectX, intersectY, ae1.botX, ae1.botY) then updatedZ <- ae1.botZ
                elif xyEqual (intersectX, intersectY, ae1.topX, ae1.topY) then updatedZ <- ae1.topZ
                elif xyEqual (intersectX, intersectY, ae2.botX, ae2.botY) then updatedZ <- ae2.botZ
                elif xyEqual (intersectX, intersectY, ae2.topX, ae2.topY) then updatedZ <- ae2.topZ
                else                                                                updatedZ <- Null.DEFZ()
                opt.z <- zcallback( Null.opt ae1, Null.opt ae2, intersectX, intersectY, updatedZ)
            else
                if   xyEqual (intersectX, intersectY, ae2.botX, ae2.botY) then updatedZ <- ae2.botZ
                elif xyEqual (intersectX, intersectY, ae2.topX, ae2.topY) then updatedZ <- ae2.topZ
                elif xyEqual (intersectX, intersectY, ae1.botX, ae1.botY) then updatedZ <- ae1.botZ
                elif xyEqual (intersectX, intersectY, ae1.topX, ae1.topY) then updatedZ <- ae1.topZ
                else                                                                updatedZ <- Null.DEFZ()
                opt.z <- zcallback(Null.opt ae2, Null.opt ae1, intersectX, intersectY, updatedZ)


    // #endregion
    // #region AEL management

    // AEL: 'active edge list' (Vatti's AET - active edge table)
    //     a linked list of all edges (from left to right) that are present
    //     (or 'active') within the current scanbeam (a horizontal 'beam' that
    //     sweeps from bottom to top over the paths in the clipping operation).

    let deleteFromAEL (ae: ActiveEdge<'Z>) : unit =
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
        while isNotNull actives do
            deleteFromAEL actives
        scanlineArr.Clear()
        scanlineHeapSet.Clear()
        intersectList |> Rarr.clear //disposeIntersectNodes()
        outrecList    |> Rarr.clear
        horzSegList   |> Rarr.clear
        horzJoinList  |> Rarr.clear


    let insertScanline (y: float) : unit =
        if useScanlineArray then
            // when the pending scanline count outgrows the threshold, upgrade to the heap+set container
            if scanlineArr.Insert y && scanlineArr.Count > scanlineArrayThreshold then
                scanlineArr.DrainInto scanlineHeapSet
                useScanlineArray <- false
        else
            scanlineHeapSet.Insert y

    // Returns the next scanline Y coordinate, or NaN if there are no more.
    let popScanline () : float =
        if useScanlineArray then scanlineArr.Pop()
        else scanlineHeapSet.Pop()



    // ---- OutRec<'Z> helpers ----

    let newOutRec () : OutRec<'Z> =
        let result : OutRec<'Z> = {
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
            path =  Geo.emptyPath64<'Z> hasZValues
            isOpen = false
            splits = null'()
            recursiveSplit = null'()
        }
        result.idx <- outrecList |> Rarr.len
        outrecList.Add(result)
        result


    // #endregion
    // #region Point in polygon
    let getCleanPath (op: OutPt<'Z>) : Path64<'Z> =
        let result = Geo.emptyPath64<'Z> hasZValues
        let mutable op2 = op
        while op2.next =!= op && ((isEqualTol op2.x op2.next.x && isEqualTol op2.x op2.prev.x) || (isEqualTol op2.y op2.next.y && isEqualTol op2.y op2.prev.y)) do
            op2 <- op2.next
        result.Add(op2.x, op2.y, op2.z)
        let mutable prevOp = op2
        op2 <- op2.next
        while op2 =!= op do
            if (isNotEqualTol op2.x op2.next.x || isNotEqualTol op2.x prevOp.x) && (isNotEqualTol op2.y op2.next.y || isNotEqualTol op2.y prevOp.y) then
                result.Add(op2.x, op2.y, op2.z)
                prevOp <- op2
            op2 <- op2.next
        result

    let path1InsidePath2 (op1: OutPt<'Z>, op2: OutPt<'Z>) : bool =
        // allow rounding noise: skip if the first vertex appears outside
        let mutable pip = PointInPolygonResult.IsOn
        let mutable op = op1
        let mutable result = false
        let mutable finished = false
        let mutable loopOn = true
        while loopOn do
            match Eng.pointInOpPolygon coordEqTol colinTolSqrd op.x op.y op2 with
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
            Geo.path2ContainsPath1 coordEqTol colinTolSqrd (getCleanPath op1) (getCleanPath op2)

    // #endregion
    // #region Horizontal segments / joins

    let setHorzSegHeadingForward (hs: HorzSegment<'Z>, opP: OutPt<'Z>, opN: OutPt<'Z>) : bool =
        if isEqualTol opP.x opN.x then
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

    let updateHorzSegment (hs: HorzSegment<'Z>) : bool =
        let op = hs.leftOp
        let outrec = Eng.getRealOutRec op.outrec
        let outrecHasEdges = isNotNull outrec.frontEdge
        let currY = op.y
        let mutable opP = op
        let mutable opN = op

        if outrecHasEdges then
            let opA = outrec.pts
            let opZ = opA.next
            while opP =!= opZ && isEqualTol opP.prev.y currY do
                opP <- opP.prev
            while opN =!= opA && isEqualTol opN.next.y currY do
                opN <- opN.next
        else
            while opP.prev =!= opN && isEqualTol opP.prev.y currY do
                opP <- opP.prev
            while opN.next =!= opP && isEqualTol opN.next.y currY do
                opN <- opN.next

        let result = setHorzSegHeadingForward(hs, opP, opN) && isNull' hs.leftOp.horz

        if result then
            hs.leftOp.horz <- hs
        else
            hs.rightOp <- null'() // (for sorting)
        result


    // #endregion
    // #region Edges / adding output points

    let addOutPt (ae: ActiveEdge<'Z>, ptX: float, ptY: float, ptZ: 'Z) : OutPt<'Z> =
        let outrec = ae.outrec
        let toFront = Eng.isFront ae
        let opFront = outrec.pts
        let opBack = opFront.next

        if toFront && xyEqual(ptX, ptY, opFront.x, opFront.y) then
            opFront
        elif not toFront && xyEqual(ptX, ptY, opBack.x, opBack.y) then
            opBack
        else
            let newOp = OutPt<'Z>.create (ptX, ptY, ptZ, outrec)
            opBack.prev <- newOp
            newOp.prev <- opFront
            newOp.next <- opBack
            opFront.next <- newOp
            if toFront then
                outrec.pts <- newOp
            newOp

    let addToHorzSegList (op: OutPt<'Z>) : unit =
        if op.outrec.isOpen then
            ()
        else
            horzSegList.Add {
                    HorzSegment.leftOp = op
                    HorzSegment.rightOp = null'()
                    HorzSegment.leftToRight = true
                }

    let startOpenPath (ae: ActiveEdge<'Z>, ptX: float, ptY: float, ptZ: 'Z) : OutPt<'Z> =
        let outrec = newOutRec()
        outrec.isOpen <- true
        if ae.windDx > 0 then
            outrec.frontEdge <- ae
            outrec.backEdge <- null'()
        else
            outrec.frontEdge <- null'()
            outrec.backEdge <- ae
        ae.outrec <- outrec
        let op = OutPt<'Z>.create (ptX, ptY, ptZ, outrec)
        outrec.pts <- op
        op


    let addLocalMinPoly (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>, ptX: float, ptY: float, ptZ: 'Z, isNew: bool) : OutPt<'Z> =
        let outrec = newOutRec()
        ae1.outrec <- outrec
        ae2.outrec <- outrec

        if isOpen ae1 then
            outrec.owner <- null'()
            outrec.isOpen <- true
            if ae1.windDx > 0 then
                Eng.setSides(outrec, ae1, ae2)
            else
                Eng.setSides(outrec, ae2, ae1)
        else
            outrec.isOpen <- false
            let prevHotEdge = getPrevHotEdge ae1
            if isNotNull prevHotEdge then
                if usingPolytree then
                    Eng.setOwner(outrec, prevHotEdge.outrec)
                outrec.owner <- prevHotEdge.outrec
                if Eng.outrecIsAscending prevHotEdge = isNew then
                    Eng.setSides(outrec, ae2, ae1)
                else
                    Eng.setSides(outrec, ae1, ae2)
            else
                outrec.owner <- null'()
                if isNew then
                    Eng.setSides(outrec, ae1, ae2)
                else
                    Eng.setSides(outrec, ae2, ae1)

        let op = OutPt<'Z>.create (ptX, ptY, ptZ, outrec)
        outrec.pts <- op
        op

    let joinOutrecPaths (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>) : unit =
        let p1Start = ae1.outrec.pts
        let p2Start = ae2.outrec.pts
        let p1End = p1Start.next
        let p2End = p2Start.next
        if Eng.isFront ae1 then
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
        Eng.setOwner(ae2.outrec, ae1.outrec)

        if isOpenEnd ae1 then
            ae2.outrec.pts <- ae1.outrec.pts
            ae1.outrec.pts <- null'()

        ae1.outrec <- null'()
        ae2.outrec <- null'()

    let swapOutrecs (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>) : unit =
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

    let isJoined (e: ActiveEdge<'Z>) : bool =
        e.joinWith <> JoinWith.None

    let split (e: ActiveEdge<'Z>, currX: float, currY: float, currZ: 'Z) : unit =
        if e.joinWith = JoinWith.Right then
            e.joinWith <- JoinWith.None
            e.nextInAEL.joinWith <- JoinWith.None
            addLocalMinPoly(e, e.nextInAEL, currX, currY, currZ, true) |> ignore
        else
            e.joinWith <- JoinWith.None
            e.prevInAEL.joinWith <- JoinWith.None
            addLocalMinPoly(e.prevInAEL, e, currX, currY, currZ, true) |> ignore

    let addLocalMaxPoly (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>, ptX: float, ptY: float, ptZ: 'Z) : OutPt<'Z> =
        if isJoined ae1 then
            split(ae1, ptX, ptY, ptZ)
        if isJoined ae2 then
            split(ae2, ptX, ptY, ptZ)

        if Eng.isFront ae1 = Eng.isFront ae2 then
            if isOpenEnd ae1 then
                Eng.swapFrontBackSides ae1.outrec
            elif isOpenEnd ae2 then
                Eng.swapFrontBackSides ae2.outrec
            else
                // in original Clipper2, this just aborted and returnd null, leadien to .Execute returning false and no solution.
                invalidOp "Klip: invalid state in addLocalMaxPoly: both edges are on the same side of their respective OutRecs and neither is an open end"

        let result = addOutPt(ae1, ptX, ptY, ptZ)
        if ae1.outrec === ae2.outrec then
            let outrec = ae1.outrec
            outrec.pts <- result
            if usingPolytree then
                let e = getPrevHotEdge ae1
                if isNull' e then
                    outrec.owner <- null'()
                else
                    Eng.setOwner(outrec, e.outrec)
            Eng.uncoupleOutRec ae1
        elif isOpen ae1 then
            if ae1.windDx < 0 then
                joinOutrecPaths(ae1, ae2)
            else
                joinOutrecPaths(ae2, ae1)
        elif ae1.outrec.idx < ae2.outrec.idx then
            joinOutrecPaths(ae1, ae2)
        else
            joinOutrecPaths(ae2, ae1)
        result

    // #endregion
    // #region AEL insertion / validation

    // AEL: 'active edge list' (Vatti's AET - active edge table)
    //     a linked list of all edges (from left to right) that are present
    //     (or 'active') within the current scanbeam (a horizontal 'beam' that
    //     sweeps from bottom to top over the paths in the clipping operation).

    let rec isValidAelOrder (resident: ActiveEdge<'Z>, newcomer: ActiveEdge<'Z>) : bool =
        if isNotEqualTol newcomer.curX resident.curX then
            newcomer.curX > resident.curX
        else
            let d = Geo.crossProductSign colinTolSqrd (resident.topX, resident.topY, newcomer.botX, newcomer.botY, newcomer.topX, newcomer.topY)
            if d <> 0 then
                d < 0
            else
                if (not (Eng.isMaximaA resident)) && resident.topY > newcomer.topY then
                    let nextResident = Eng.nextVertex resident
                    Geo.crossProductSign colinTolSqrd (newcomer.botX, newcomer.botY, resident.topX, resident.topY, nextResident.x, nextResident.y) <= 0
                elif (not (Eng.isMaximaA newcomer)) && newcomer.topY > resident.topY then
                    let nextNewcomer = Eng.nextVertex newcomer
                    Geo.crossProductSign colinTolSqrd (newcomer.botX, newcomer.botY, newcomer.topX, newcomer.topY, nextNewcomer.x, nextNewcomer.y) >= 0
                else
                    let y = newcomer.botY
                    let newcomerIsLeft = newcomer.isLeftBound
                    if resident.botY <> y || resident.localMin.vertex.y <> y then
                        newcomer.isLeftBound
                    elif resident.isLeftBound <> newcomerIsLeft then
                        newcomerIsLeft
                    elif Geo.isColinear (colinTolSqrd, (Eng.prevPrevVertex resident).x, (Eng.prevPrevVertex resident).y, resident.botX, resident.botY, resident.topX, resident.topY) then
                        true
                    else
                        let cross = Geo.crossProductSign colinTolSqrd (
                                        (Eng.prevPrevVertex resident).x,
                                        (Eng.prevPrevVertex resident).y,
                                        newcomer.botX,
                                        newcomer.botY,
                                        (Eng.prevPrevVertex newcomer).x,
                                        (Eng.prevPrevVertex newcomer).y)
                        cross > 0  = newcomerIsLeft

    let insertLeftEdge (ae: ActiveEdge<'Z>) : unit =
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

    let insertRightEdge (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>) : unit =
        ae2.nextInAEL <- ae1.nextInAEL
        if isNotNull ae1.nextInAEL then
            ae1.nextInAEL.prevInAEL <- ae2
        ae2.prevInAEL <- ae1
        ae1.nextInAEL <- ae2

    let swapPositionsInAEL (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>) : unit =
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

    let pushHorz (ae: ActiveEdge<'Z>) : unit =
        ae.nextInSEL <- sel
        sel <- ae

    let popHorz () : ActiveEdge<'Z> =
        if isNull' sel then
            null'()
        else
            let ae = sel
            sel <- sel.nextInSEL
            ae

    // ---- Wind counts ----

    let setWindCountForOpenPathEdge (ae: ActiveEdge<'Z>) : unit =
        let mutable ae2 = actives
        if fillrule = FillRule.EvenOdd then
            let mutable cnt1 = 0
            let mutable cnt2 = 0
            while ae2 =!= ae do
                if ae2.localMin.pathType = PathType.Clip then
                    cnt2 <- cnt2 + 1
                elif not (isOpen ae2) then
                    cnt1 <- cnt1 + 1
                ae2 <- ae2.nextInAEL
            ae.windCount  <- (if Eng.isOdd cnt1 then 1 else 0)
            ae.windCount2 <- (if Eng.isOdd cnt2 then 1 else 0)
        else
            while ae2 =!= ae do
                if ae2.localMin.pathType = PathType.Clip then
                    ae.windCount2 <- ae.windCount2 + ae2.windDx
                elif not (isOpen ae2) then
                    ae.windCount <- ae.windCount + ae2.windDx
                ae2 <- ae2.nextInAEL

    let setWindCountForClosedPathEdge (ae: ActiveEdge<'Z>) : unit =
        let mutable ae2 = ae.prevInAEL
        let pt = ae.localMin.pathType

        if not openPathsEnabled then
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
            while isNotNull ae2 && (ae2.localMin.pathType <> pt || isOpen ae2) do
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
                        ae.windCount <- (if isOpen ae then 1 else ae.windDx)
                else
                    if ae2.windDx * ae.windDx < 0 then
                        ae.windCount <- ae2.windCount
                    else
                        ae.windCount <- ae2.windCount + ae.windDx
                ae.windCount2 <- ae2.windCount2
                ae2 <- ae2.nextInAEL

            if fillrule = FillRule.EvenOdd then
                while ae2 =!= ae do
                    if ae2.localMin.pathType <> pt && not (isOpen ae2) then
                        ae.windCount2 <- (if ae.windCount2 = 0 then 1 else 0)
                    ae2 <- ae2.nextInAEL
            else
                while ae2 =!= ae do
                    if ae2.localMin.pathType <> pt && not (isOpen ae2) then
                        ae.windCount2 <- ae.windCount2 + ae2.windDx
                    ae2 <- ae2.nextInAEL

    let isContributingOpen (ae: ActiveEdge<'Z>) : bool =
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

    let isContributingClosed (ae: ActiveEdge<'Z>) : bool =
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

    // Near-top join guard. Adjacent-edge joins are suppressed when the candidate join
    // point sits at (or just below, in scanline-Y terms) an edge's top vertex, because a
    // join that close to a maximum is almost always a maximum being processed, not a real
    // touching-edge merge. The window is edge-height-relative (`height * nearTopYToleranceFactor`)
    // so tall edges get a proportionally larger margin, but it is capped at an absolute
    // `nearTopYToleranceCap` so very tall edges don't get an unboundedly wide guard.
    // Both constants are tunable via the NearTopYToleranceFactor / NearTopYToleranceCap properties.
    let isNearOrAboveTopY (ptY: float) (edge: ActiveEdge<'Z>) : bool =
        let topTol = min nearTopYToleranceCap (Math.Abs(edge.botY - edge.topY) * nearTopYToleranceFactor)
        ptY < edge.topY + topTol

    let checkJoinLeft (ae: ActiveEdge<'Z>, ptX: float, ptY: float, ptZ: 'Z, checkCurrX: bool) : unit =
        let prev = ae.prevInAEL
        if isNull' prev
            || not (Eng.isHotEdge ae)
            || not (Eng.isHotEdge prev)
            || isHorizontal ae
            || isHorizontal prev
            || isOpen ae
            || isOpen prev then
                ()
        elif (isNearOrAboveTopY ptY ae || isNearOrAboveTopY ptY prev) && (ae.botY > ptY || prev.botY > ptY) then
                ()
        else
            let earlyExit =
                if checkCurrX then
                    Eng.distFromLineSqrdGreaterThanTolerance ( mergeVertexToleranceSqrd, ptX, ptY, prev.botX, prev.botY, prev.topX, prev.topY)
                else
                    isNotEqualTol ae.curX prev.curX
            if earlyExit then
                ()
            elif not (Geo.isColinear (colinTolSqrd, ae.topX, ae.topY, ptX, ptY, prev.topX, prev.topY)) then
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

    let checkJoinRight (ae: ActiveEdge<'Z>, ptX: float, ptY: float, ptZ: 'Z, checkCurrX: bool) : unit =
        let next = ae.nextInAEL
        if isNull' next
           || not (Eng.isHotEdge ae)
           || not (Eng.isHotEdge next)
           || isHorizontal ae
           || isHorizontal next
           || isOpen ae
           || isOpen next then
                ()
        elif (isNearOrAboveTopY ptY ae || isNearOrAboveTopY ptY next) && (ae.botY > ptY || next.botY > ptY) then
                ()
        else
            let earlyExit =
                if checkCurrX then
                    Eng.distFromLineSqrdGreaterThanTolerance ( mergeVertexToleranceSqrd, ptX, ptY, next.botX, next.botY, next.topX, next.topY)
                else
                    isNotEqualTol ae.curX next.curX
            if earlyExit then
                ()
            elif not (Geo.isColinear (colinTolSqrd, ae.topX, ae.topY, ptX, ptY, next.topX, next.topY)) then
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

    // #endregion
    // #region Intersect edges

    let findEdgeWithMatchingLocMin (e: ActiveEdge<'Z>) : ActiveEdge<'Z> =
        let mutable result = e.nextInAEL
        let mutable loopOn = true
        while loopOn && isNotNull result do
            if Eng.localMinimaEqual(result.localMin, e.localMin) then
                loopOn <- false
            elif not (isHorizontal result) &&
                 not (xyEqual(e.botX, e.botY, result.botX, result.botY)) then
                result <- null'()
            else
                result <- result.nextInAEL
        if isNotNull result then
            result
        else
            result <- e.prevInAEL
            let mutable loopOn = true
            let mutable finalResult: ActiveEdge<'Z> = null'()
            while loopOn && isNotNull result do
                if Eng.localMinimaEqual(result.localMin, e.localMin) then
                    finalResult <- result
                    loopOn <- false
                elif not (isHorizontal result) &&
                     not (xyEqual(e.botX, e.botY, result.botX, result.botY)) then
                    finalResult <- null'()
                    loopOn <- false
                else
                    result <- result.prevInAEL
            if not loopOn then
                finalResult
            else
                result

    /// MANAGE OPEN PATH INTERSECTIONS SEPARATELY ...
    let intersectOpenEdges (ae1In: ActiveEdge<'Z>, ae2In: ActiveEdge<'Z>, ptX: float, ptY: float, ptZ: 'Z) : unit =
        let mutable ae1 = ae1In
        let mutable ae2 = ae2In
        let mutable resultOp: OutPt<'Z> = null'()
        if isOpen ae1 && isOpen ae2 then
            ()
        else
            // the following line avoids duplicating quite a bit of code
            if isOpen ae2 then
                let tmp = ae1
                ae1 <- ae2
                ae2 <- tmp
            if isJoined ae2 then
                split(ae2, ptX, ptY, ptZ) // needed for safety

            let mutable cancel = false
            if cliptype = ClipType.Union then
                if not (Eng.isHotEdge ae2) then
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
                if Eng.isHotEdge ae1 then
                    resultOp <- addOutPt(ae1, ptX, ptY, ptZ)
                    // setZ is called once for resultOp below, after all branches
                    if Eng.isFront ae1 then
                        ae1.outrec.frontEdge <- null'()
                    else
                        ae1.outrec.backEdge <- null'()
                    ae1.outrec <- null'()
                // horizontal edges can pass under open paths at a LocMins
                elif isEqualTol ptX ae1.localMin.vertex.x && isEqualTol ptY ae1.localMin.vertex.y && not (Eng.isOpenEndVertex ae1.localMin.vertex) then
                    // find the other side of the LocMin and
                    // if it's 'hot' join up with it ...
                    let ae3 = findEdgeWithMatchingLocMin ae1
                    if isNotNull ae3 && Eng.isHotEdge ae3 then
                        ae1.outrec <- ae3.outrec
                        if ae1.windDx > 0 then
                            Eng.setSides(ae3.outrec, ae1, ae3)
                        else
                            Eng.setSides(ae3.outrec, ae3, ae1)
                    else
                        resultOp <- startOpenPath(ae1, ptX, ptY, ptZ)
                else
                    resultOp <- startOpenPath(ae1, ptX, ptY, ptZ)
                if isNotNull resultOp then
                    setZ(ae1, ae2, resultOp)

    /// MANAGING CLOSED PATHS FROM HERE ON
    let intersectClosedPathEdges (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>, ptX: float, ptY: float, ptZ: 'Z) : unit =
        let mutable resultOp: OutPt<'Z> = null'()

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

        if not (Eng.isHotEdge ae1) && not e1WindCountIs0or1 || not (Eng.isHotEdge ae2) && not e2WindCountIs0or1 then
            ()

        elif Eng.isHotEdge ae1 && Eng.isHotEdge ae2 then
            // NOW PROCESS THE INTERSECTION ...

            // if both edges are 'hot' ...
            if (oldE1WindCount <> 0 && oldE1WindCount <> 1) || (oldE2WindCount <> 0 && oldE2WindCount <> 1) || (ae1.localMin.pathType <> ae2.localMin.pathType && cliptype <> ClipType.Xor) then
                // this 'else if' condition isn't strictly needed but
                // it's sensible to split polygons that only touch at
                // a common vertex (not at common edges).
                resultOp <- addLocalMaxPoly(ae1, ae2, ptX, ptY, ptZ)
                if isNotNull resultOp then
                    setZ(ae1, ae2, resultOp)
            elif Eng.isFront ae1 || ae1.outrec === ae2.outrec then
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
        elif Eng.isHotEdge ae1 then
            resultOp <- addOutPt(ae1, ptX, ptY, ptZ)
            setZ(ae1, ae2, resultOp)
            swapOutrecs(ae1, ae2)
        elif Eng.isHotEdge ae2 then
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

            if not (Eng.isSamePolyType(ae1, ae2)) then
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

    let intersectEdges (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>, ptX: float, ptY: float, ptZ: 'Z) : unit =
        if hasOpenPaths && (isOpen ae1 || isOpen ae2) then
            intersectOpenEdges(ae1, ae2, ptX, ptY, ptZ)
        else
            intersectClosedPathEdges(ae1, ae2, ptX, ptY, ptZ)


    // ---- SEL helpers ----
    // SEL: 'sorted edge list' (Vatti's ST - sorted table)
    //     linked list used when sorting edges into their new positions at the
    //     top of scanbeams, but also (re)used to process horizontals.

    let extractFromSEL (ae: ActiveEdge<'Z>) : ActiveEdge<'Z> =
        let res = ae.nextInSEL
        if isNotNull res then
            res.prevInSEL <- ae.prevInSEL
        ae.prevInSEL.nextInSEL <- res
        res

    let insert1Before2InSEL (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>) : unit =
        ae1.prevInSEL <- ae2.prevInSEL
        if isNotNull ae1.prevInSEL then
            ae1.prevInSEL.nextInSEL <- ae1
        ae1.nextInSEL <- ae2
        ae2.prevInSEL <- ae1

    /// At the bottom of a scanbeam, AEL is already correctly ordered.
    /// But at the top of that scanbeam, edges may have crossed, so their order may need to change.
    /// adjustCurrXAndCopyToSEL copies the AEL links into SEL links and updates every edge’s curX to where it will be at topY:
    let adjustCurrXAndCopyToSEL (topY: float) : unit =
        let mutable ae = actives
        sel <- ae
        while isNotNull ae do
            ae.prevInSEL <- ae.prevInAEL
            ae.nextInSEL <- ae.nextInAEL
            ae.jump <- ae.nextInSEL
            ae.curX <- Eng.topX(ae, topY)
            ae <- ae.nextInAEL

    let addNewIntersectNode (ae1: ActiveEdge<'Z>, ae2: ActiveEdge<'Z>, topY: float) : unit =
        let xNode = Eng.getLineIntersectPt (ae1, ae2, topY)

        // NB: the corrections below move xNode's x/y but leave xNode.z as assigned by
        // getLineIntersectPt, so z may describe the uncorrected point. This is acceptable:
        // setZ later re-resolves Z against the edge endpoints by coordinate, so a stale z
        // only survives when no Z callback is set (where z is unread metadata anyway).
        if xNode.y > currentBotY || xNode.y < topY then
            let absDx1 = Math.Abs(ae1.dx)
            let absDx2 = Math.Abs(ae2.dx)
            if absDx1 > 100.0 && absDx2 > 100.0 then
                if absDx1 > absDx2 then
                    Eng.getClosestPtOnSegment (xNode, ae1.botX, ae1.botY, ae1.topX, ae1.topY)
                else
                    Eng.getClosestPtOnSegment (xNode, ae2.botX, ae2.botY, ae2.topX, ae2.topY)
            elif absDx1 > 100.0 then
                Eng.getClosestPtOnSegment (xNode, ae1.botX, ae1.botY, ae1.topX, ae1.topY)
            elif absDx2 > 100.0 then
                Eng.getClosestPtOnSegment (xNode, ae2.botX, ae2.botY, ae2.topX, ae2.topY)
            else
                if xNode.y < topY then
                    xNode.y <- topY
                else
                    xNode.y <- currentBotY

                if absDx1 < absDx2 then
                    xNode.x <- Eng.topX(ae1, xNode.y)
                else
                    xNode.x <- Eng.topX(ae2, xNode.y)

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
                let mutable prevBase: ActiveEdge<'Z> = null'()
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

            intersectList |> Rarr.len > 0



    let processIntersectList () : unit =
        // We now have a list of intersections required so that edges will be
        // correctly positioned at the top of the scanbeam. However, it's important
        // that edge intersections are processed from the bottom up, but it's also
        // crucial that intersections only occur between adjacent edges.

        // First we do a quicksort so intersections proceed in a bottom up order ...
        // printfn $"sorting {intersectList.Count} intersections"
        intersectList.Sort Eng.intersectListSort

        for i = 0 to intersectList |> Rarr.lastIdx do
            if not (Eng.edgesAdjacentInAEL(Rarr.getIdx i intersectList)) then
                let mutable j = i + 1
                while not (Eng.edgesAdjacentInAEL(Rarr.getIdx j intersectList)) do
                    j <- j + 1
                // swap positions i and j
                let tmp = Rarr.getIdx i intersectList
                intersectList |> Rarr.setIdx i (Rarr.getIdx j intersectList)
                intersectList |> Rarr.setIdx j tmp

            let node = Rarr.getIdx i intersectList
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

    // #endregion
    // #region Horizontal processing

    let updateEdgeIntoAEL (ae: ActiveEdge<'Z>) : unit =
        // Avoid copying points (and avoid materializing z=0) for speed.
        ae.botX <- ae.topX
        ae.botY <- ae.topY
        ae.botZ <- ae.topZ
        ae.vertexTop <- Eng.nextVertex ae
        ae.topX <- ae.vertexTop.x
        ae.topY <- ae.vertexTop.y
        ae.topZ <- ae.vertexTop.z
        ae.curX <- ae.botX
        Eng.setDx (horzAngleTol, ae)

        if isJoined ae then split(ae, ae.botX, ae.botY, ae.botZ)

        if isHorizontal ae then
            if not (isOpen ae) then
                Eng.trimHorz(horzAngleTol, ae, preserveColinear)
        else
            insertScanline ae.topY
            checkJoinLeft(ae, ae.botX, ae.botY, ae.botZ, false)
            checkJoinRight(ae, ae.botX, ae.botY, ae.botZ, true)

    let doHorizontal (horz: ActiveEdge<'Z>) : unit =
        let horzIsOpen = isOpen horz
        let y = horz.botY

        let vertexMax =
            if horzIsOpen then
                Eng.getCurrYMaximaVertexOpen horz
            else
                Eng.getCurrYMaximaVertex horz

        // Clipper2 assumes that when a horizontal bound ends at a local maximum, the
        // maxima-pair edge is already in the AEL: with integer coordinates both bounds
        // reach a shared flat run at the same scanline. With unrounded coordinates the
        // opposite bound can reach the near-flat (tolerance-horizontal) run at a
        // slightly different exact Y, i.e. a later scanbeam, so the pair can be absent.
        // The inherited fall-through would then walk this edge past the maximum and back
        // *up* the opposite bound, re-inserting an already-swept scanline — an endless
        // scanbeam ping-pong. Detect the absent pair up front: the range checks in the
        // loop below stay active (no pass-overs hunting an edge that is not there) and,
        // instead of advancing past the maximum, the edge is parked in the AEL at its
        // top, where the opposite bound finds it as its own maxima pair a few beams
        // later (mirroring doMaxima, which also leaves the edge alone on a null pair).
        let maxPairInAEL =
            if isNull' vertexMax then
                false
            else
                let mutable ae = actives
                let mutable found = false
                while not found && isNotNull ae do
                    if ae =!= horz && ae.vertexTop === vertexMax then
                        found <- true
                    ae <- ae.nextInAEL
                found

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


        if Eng.isHotEdge horz then
            let op = addOutPt(horz, horz.curX, y, Null.DEFZ())
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
            while not innerBreak && isNotNull ae && not returned do
                if ae.vertexTop === vertexMax then
                    if Eng.isHotEdge horz && isJoined ae then
                        split(ae, ae.topX, ae.topY, ae.topZ)
                    if Eng.isHotEdge horz then
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
                    if (vertexMax =!= horz.vertexTop) || isOpenEnd horz || not maxPairInAEL then
                        if (isLeftToRight && ae.curX > rightX2) || (not isLeftToRight && ae.curX < leftX2) then
                            localBreak <- true
                        elif isEqualTol ae.curX horz.topX && not (isHorizontal ae) then
                            let nextPt = Eng.nextVertex horz
                            if isOpen ae && not (Eng.isSamePolyType(ae, horz)) && not (Eng.isHotEdge ae) then
                                if (isLeftToRight && Eng.topX(ae, nextPt.y) > nextPt.x) || (not isLeftToRight && Eng.topX(ae, nextPt.y) < nextPt.x) then
                                     localBreak <- true
                            else
                                if (isLeftToRight && Eng.topX(ae, nextPt.y) >= nextPt.x)|| (not isLeftToRight && Eng.topX(ae, nextPt.y) <= nextPt.x) then
                                    localBreak <- true

                    if localBreak then
                        innerBreak <- true
                    else
                        let ptX = ae.curX
                        let ptY = y
                        let ptZ = Null.DEFZ()
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

                        if Eng.isHotEdge horz then
                            addToHorzSegList (Eng.getLastOp horz)

            if not returned then
                if horzIsOpen && isOpenEnd horz then
                    if Eng.isHotEdge horz then
                        addOutPt(horz, horz.topX, horz.topY, horz.topZ) |> ignore
                        if Eng.isFront horz then
                            horz.outrec.frontEdge <- null'()
                        else
                            horz.outrec.backEdge <- null'()
                        horz.outrec <- null'()
                    deleteFromAEL horz
                    returned <- true
                elif (Eng.nextVertex horz).y <> horz.topY then
                    outerLoop <- false
                else
                    // still more horizontals in bound to process
                    if Eng.isHotEdge horz then
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
            if vertexMax === horz.vertexTop && not (isOpenEnd horz) && not maxPairInAEL then
                // The maxima pair is not in the AEL yet (see maxPairInAEL above): park
                // this edge at its top instead of advancing past the local maximum. It
                // stays in the AEL until the opposite bound arrives and claims it as its
                // maxima pair; the closing OutPt is added by that addLocalMaxPoly.
                ()
            else
                if Eng.isHotEdge horz then
                    let op = addOutPt(horz, horz.topX, horz.topY, horz.topZ)
                    addToHorzSegList op
                updateEdgeIntoAEL horz
                // The loop above exits on an exact next-vertex-Y mismatch, but with the
                // tolerance-based isHorizontal the next segment can still classify as
                // horizontal (a near-flat continuation, e.g. 37 vs 36.999999999). In that
                // case updateEdgeIntoAEL inserted no scanline for its top, so re-queue it
                // for horizontal processing — mirroring doTopOfScanbeam — or the edge is
                // orphaned in the AEL with dx = ±infinity and corrupts curX at the next beam.
                if isHorizontal horz then
                    pushHorz horz


    // #endregion
    // #region Local minima insertion

    /// this is the first function called in .Execute()
    /// the minima list is build up during .AddPath() calls, but is not sorted until now.
    /// We need to sort it before we can start processing scanlines.
    let sortMinimaResetScanlines () : unit =
        let minimaList = minimaList // avoiding access via 'this' in JS
        if not isSortedMinimaList then
            // printfn "Sorting minima list with %d items" minimaList.Count
            minimaList.Sort Eng.minimaListCmp
            isSortedMinimaList <- true

        scanlineArr.Clear()
        scanlineHeapSet.Clear()
        // Heuristic: local minima count correlates with number of scanlines and
        // scanline insert/pop activity. For glyph-like inputs, this is typically small.
        // Should the pending count outgrow the threshold mid-sweep, insertScanline upgrades to the heap+set.
        useScanlineArray <- Rarr.len minimaList <= scanlineArrayThreshold
        for i = Rarr.lastIdx minimaList downto 0 do
            insertScanline (Rarr.getIdx i minimaList).vertex.y

        currentBotY <- 0.0
        currentLocMin <- 0
        actives <- null'()
        sel <- null'()

    let insertLocalMinimaIntoAEL (botY: float) : unit =
        let minimaList = minimaList // avoiding access via 'this' in JS

        let inline hasLocMinAtY (y: float) : bool =
            currentLocMin < Rarr.len minimaList &&
            (Rarr.getIdx currentLocMin minimaList).vertex.y = y

        let inline popLocalMinima () : LocalMinima<'Z> =
            let result = Rarr.getIdx currentLocMin minimaList
            currentLocMin <- currentLocMin + 1
            result

        // Add any local minima (if any) at BotY ...
        // NB horizontal local minima edges should contain locMin.vertex.prev
        while hasLocMinAtY botY do
            let localMinima = popLocalMinima()
            let vertex = localMinima.vertex
            let mutable leftBound: ActiveEdge<'Z> = null'()

            if (vertex.flags &&& VertexFlags.OpenStart) <> VertexFlags.None then
                leftBound <- null'()
            else
                leftBound <- ActiveEdge.create(
                    horzAngleTol,
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

            let mutable rightBound: ActiveEdge<'Z> = null'()
            if (vertex.flags &&& VertexFlags.OpenEnd) <> VertexFlags.None then
                rightBound <- null'()
            else
                rightBound <- ActiveEdge.create(
                    horzAngleTol,
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
                if isHorizontal leftBound then
                    if Eng.isHeadingRightHorz leftBound then
                        let tmp = leftBound
                        leftBound <- rightBound
                        rightBound <- tmp
                elif isHorizontal rightBound then
                    if Eng.isHeadingLeftHorz rightBound then
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

            if not openPathsEnabled then
                setWindCountForClosedPathEdge leftBound
                contributing <- isContributingClosed leftBound
            elif isOpen leftBound then
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
                    if not (isHorizontal leftBound) then
                        checkJoinLeft(leftBound, leftBound.botX, leftBound.botY, leftBound.botZ, false)

                while isNotNull rightBound.nextInAEL && isValidAelOrder(rightBound.nextInAEL, rightBound) do
                    intersectEdges(rightBound, rightBound.nextInAEL, rightBound.botX, rightBound.botY, rightBound.botZ)
                    swapPositionsInAEL(rightBound, rightBound.nextInAEL)

                if isHorizontal rightBound then
                    pushHorz rightBound
                else
                    checkJoinRight(rightBound, rightBound.botX, rightBound.botY, rightBound.botZ, false)
                    insertScanline rightBound.topY
            elif contributing && openPathsEnabled then
                startOpenPath(leftBound, leftBound.botX, leftBound.botY, leftBound.botZ) |> ignore

            if isHorizontal leftBound then
                pushHorz leftBound
            else
                insertScanline leftBound.topY

    // #endregion
    // #region  Horz segments Joins

    let convertHorzSegsToJoins () : unit =
        let horzSegList = horzSegList // avoiding access via 'this' in JS
        let mutable k = 0
        for i = 0 to horzSegList |> Rarr.lastIdx do
            let hs = Rarr.getIdx i horzSegList
            if updateHorzSegment hs then
                k <- k + 1
        if k < 2 then
            ()
        else

            horzSegList.Sort Eng.horzSegSort

            for i = 0 to k - 2 do
                let hs1 = Rarr.getIdx i horzSegList
                let mutable j = i + 1
                let mutable scanOn = true
                while scanOn && j <= k - 1 do
                    let hs2 = Rarr.getIdx j horzSegList
                    // Strict-overlap test, deliberately exact (as in Clipper2): segments whose
                    // X-ranges merely ABUT (zero-length overlap) must NOT join — contours that
                    // touch at a single point are valid separate outputs (e.g. the two XOR lobes
                    // of overlapping squares), and a point-join would pinch them into a figure-8
                    // and strand the op-walks below without their x-range stopper. A noisy shared
                    // seam is unaffected: its true overlap is far larger than float noise, so the
                    // strict test still sees it; near-vertical noisy seams merge via
                    // MergeVertexTolerance in checkJoinLeft/checkJoinRight instead.
                    if hs2.leftOp.x >= hs1.rightOp.x then
                        // the list is sorted by leftOp.x, so no later hs2 can overlap hs1 either (break, as in Clipper2)
                        scanOn <- false
                    elif hs2.leftToRight = hs1.leftToRight || hs2.rightOp.x <= hs1.leftOp.x then
                        ()
                    else
                        let currY = hs1.leftOp.y
                        if hs1.leftToRight then
                            while isEqualTol hs1.leftOp.next.y currY && hs1.leftOp.next.x <= hs2.leftOp.x do
                                hs1.leftOp <- hs1.leftOp.next
                            while isEqualTol hs2.leftOp.prev.y currY && hs2.leftOp.prev.x <= hs1.leftOp.x do
                                hs2.leftOp <- hs2.leftOp.prev
                            horzJoinList.Add{
                                        op1 = Eng.duplicateOp(hs1.leftOp, true)
                                        op2 = Eng.duplicateOp(hs2.leftOp, false) }
                        else
                            while isEqualTol hs1.leftOp.prev.y currY && hs1.leftOp.prev.x <= hs2.leftOp.x do
                                hs1.leftOp <- hs1.leftOp.prev
                            while isEqualTol hs2.leftOp.next.y currY && hs2.leftOp.next.x <= hs1.leftOp.x do
                                hs2.leftOp <- hs2.leftOp.next
                            horzJoinList.Add{
                                        op1 = Eng.duplicateOp(hs2.leftOp, true)
                                        op2 = Eng.duplicateOp(hs1.leftOp, false) }
                    j <- j + 1

    let processHorzJoins () : unit =
        let horzJoinList = horzJoinList // avoiding access via 'this' in JS
        for i = 0 to horzJoinList |> Rarr.lastIdx do
            let hoj = Rarr.getIdx i horzJoinList
            let or1 = Eng.getRealOutRec hoj.op1.outrec
            let or2 = Eng.getRealOutRec hoj.op2.outrec

            let op1b = hoj.op1.next
            let op2b = hoj.op2.prev
            hoj.op1.next <- hoj.op2
            hoj.op2.prev <- hoj.op1
            op1b.prev <- op2b
            op2b.next <- op1b

            if or1 === or2 then
                let or2New = newOutRec()
                or2New.pts <- op1b
                Eng.fixOutRecPts or2New

                if or1.pts.outrec === or2New then
                    or1.pts <- hoj.op1
                    or1.pts.outrec <- or1

                if usingPolytree then
                    if path1InsidePath2(or1.pts, or2New.pts) then
                        let tmp = or2New.pts
                        or2New.pts <- or1.pts
                        or1.pts <- tmp
                        Eng.fixOutRecPts or1
                        Eng.fixOutRecPts or2New
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
                    Eng.setOwner(or2, or1)
                    Eng.moveSplits(or2, or1)
                else
                    or2.owner <- or1

    // ---- Top of scanbeam ----

    let doMaxima (ae: ActiveEdge<'Z>) : ActiveEdge<'Z> =
        let prevE = ae.prevInAEL
        let mutable nextE = ae.nextInAEL

        if isOpenEnd ae then
            if Eng.isHotEdge ae then
                addOutPt(ae, ae.topX, ae.topY, ae.topZ) |> ignore
            if isHorizontal ae then
                nextE
            else
                if Eng.isHotEdge ae then
                    if Eng.isFront ae then
                        ae.outrec.frontEdge <- null'()
                    else
                        ae.outrec.backEdge <- null'()
                    ae.outrec <- null'()
                deleteFromAEL ae
                nextE
        else
            let maxPair = Eng.getMaximaPair ae
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

                if isOpen ae then
                    if Eng.isHotEdge ae then
                        addLocalMaxPoly(ae, maxPair, ae.topX, ae.topY, ae.topZ) |> ignore
                    deleteFromAEL maxPair
                    deleteFromAEL ae
                    if isNotNull prevE then
                        prevE.nextInAEL
                    else
                        actives
                else
                    if Eng.isHotEdge ae then
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
                if Eng.isMaximaA ae then
                    ae <- doMaxima ae
                else
                    if Eng.isHotEdge ae then
                        addOutPt(ae, ae.topX, ae.topY, ae.topZ) |> ignore
                    updateEdgeIntoAEL ae
                    if isHorizontal ae then
                        pushHorz ae
                    ae <- ae.nextInAEL
            else
                ae.curX <- Eng.topX(ae, y)
                ae <- ae.nextInAEL

    // ---- Main execute loop ----

    let executeInternal (ct: ClipType, fr: FillRule) : unit =
        if ct = ClipType.NoClip then
            ()
        else
            openPathsEnabled <- hasOpenPaths
            fillrule <- fr
            cliptype <- ct
            sortMinimaResetScanlines()

            let mutable y = popScanline()
            if Double.IsNaN y then
                ()
            else
                let mutable running = true
                while running do
                    insertLocalMinimaIntoAEL y
                    let mutable ae = popHorz()
                    while isNotNull ae do
                        doHorizontal ae
                        ae <- popHorz()
                    if horzSegList |> Rarr.len > 0 then
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

                processHorzJoins()

    // #endregion
    // #region  Solution post-processing

    let doSplitOp (outrec: OutRec<'Z>, splitOp: OutPt<'Z>) : unit =
        // splitOp.prev <=> splitOp &&
        // splitOp.next <=> splitOp.next.next are intersecting
        let prevOp = splitOp.prev
        let nextNextOp = splitOp.next.next
        outrec.pts <- prevOp

        // doSplitOp is only reached when segments are known to intersect,
        // so the result is never null here.

        // this sets the tempX , tempY and tempZ fields on the module
        getLineIntersectPtInState(prevOp, splitOp, splitOp.next, nextNextOp)


        // new logic, setz only if OutPt<'Z> is actually added to the path, below after OutPt<'Z>.create (..) ,not here
        // match zCallback with
        // | Some zcallback ->
        //     // tempZ <- callback(prevOp.pt, splitOp.pt, splitOp.next.pt, nextNextOp.pt, tempX, tempY, tempZ)
        //     tempZ <- zcallback(outrec.backEdge, outrec.frontEdge , tempX, tempY, tempZ)
        // | None -> ()

        let doubleArea1 = Eng.areaOutPt<'Z> prevOp
        let absDoubleArea1 = Math.Abs(doubleArea1)

        // areaOutPt/areaTriangle return DOUBLE areas, so `2.0 * splitAreaTol` is an area
        // of splitAreaTol, and `splitAreaTol` below is an area of splitAreaTol/2.
        // (Clipper2 C# uses double-area literals 2 and 1 here; this port keeps its
        // historical 4.0/2.0 defaults via splitAreaTol = 2.0.)
        if absDoubleArea1 < 2.0 * splitAreaTol then
            outrec.pts <- null'()
        else
            let tempX = tempX
            let tempY = tempY
            let tempZ = tempZ
            let doubleArea2 = Eng.areaTriangle (tempX, tempY, splitOp.x, splitOp.y, splitOp.next.x, splitOp.next.y)
            let absDoubleArea2 = Math.Abs(doubleArea2)

            // de-link splitOp and splitOp.next from the path
            // while inserting the intersection point
            if xyEqual(tempX, tempY, prevOp.x, prevOp.y) || xyEqual(tempX, tempY, nextNextOp.x, nextNextOp.y) then
                nextNextOp.prev <- prevOp
                prevOp.next <- nextNextOp
            else
                let newOp2 = OutPt<'Z>.create (tempX, tempY, tempZ, outrec)
                newOp2.prev <- prevOp
                newOp2.next <- nextNextOp
                nextNextOp.prev <- newOp2
                prevOp.next <- newOp2
                // new logic, setz only if OutPt<'Z> is actually added to the path,
                setZ(outrec.backEdge, outrec.frontEdge, newOp2)

            // nb: area1 is the path's area *before* splitting, whereas area2 is
            // the area of the triangle containing splitOp & splitOp.next.
            // So the only way for these areas to have the same sign is if
            // the split triangle is larger than the path containing prevOp or
            // if there's more than one self=intersection.
            if not (absDoubleArea2 > splitAreaTol) || (not (absDoubleArea2 > absDoubleArea1) && ((doubleArea2 > 0.0) <> (doubleArea1 > 0.0))) then
                ()
            else
                let newOutRec = newOutRec()
                newOutRec.owner <- outrec.owner
                splitOp.outrec <- newOutRec
                splitOp.next.outrec <- newOutRec

                let newOp = OutPt<'Z>.create (tempX, tempY, tempZ, newOutRec)
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

                // new logic, setz only if OutPt<'Z> is actually added to the path,
                setZ(outrec.backEdge, outrec.frontEdge, newOp)

    let fixSelfIntersects (outrec: OutRec<'Z>) : unit =
        let mutable op2 = outrec.pts
        if op2.prev === op2.next.next then
            ()
        else
            let mutable loopOn = true
            while loopOn do
                if (isNotNull op2.next
                    && isNotNull op2.next.next
                    && Eng.boundingBoxesOverlap (op2.prev.x, op2.prev.y, op2.x, op2.y, op2.next.x, op2.next.y, op2.next.next.x, op2.next.next.y)
                    && Geo.segsIntersectNotInclusive (colinTolSqrd, op2.prev.x, op2.prev.y, op2.x, op2.y, op2.next.x, op2.next.y, op2.next.next.x, op2.next.next.y) ) then
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
    let cleanColinear (outrecIn: OutRec<'Z>) : unit =
        let outrec = Eng.getRealOutRec outrecIn
        if isNull' outrec || outrec.isOpen then
            ()
        elif not (Eng.isValidClosedPath (smallTriangleTol, outrec.pts)) then
            outrec.pts <- null'()
        else
            let mutable startOp = outrec.pts
            let mutable op: OutPt<'Z> = startOp
            let mutable loopOn = true
            let mutable invalidated = false
            while loopOn && not invalidated do
                // A vertex coincident (within coordEqTol) with a neighbour is redundant: the
                // edge between them is degenerate, so drop it independently of the colinearity
                // angle test. In integer-grid Clipper2 such points are exactly equal and the
                // cross product is exactly zero, but in unrounded mode a sub-tolerance deviation
                // (e.g. a shared vertex landing at 50 vs 50.00000000000001) makes the near-zero
                // edge fail the scale-relative colinearity test, leaving a coincident pair (and
                // the U-turn spike it props up) in the output. Otherwise fall back to the
                // colinear-spike / preserveColinear removal.
                if (isNotNull op
                    && (xyEqual(op.x, op.y, op.prev.x, op.prev.y)
                        || xyEqual(op.x, op.y, op.next.x, op.next.y)
                        || (Geo.isColinear (colinTolSqrd, op.prev.x, op.prev.y, op.x, op.y, op.next.x, op.next.y)
                            && (not preserveColinear
                                || Geo.dotProductSign (op.prev.x, op.prev.y, op.x, op.y, op.next.x, op.next.y) < 0))
                        ) ) then
                                if op === outrec.pts then
                                    outrec.pts <- op.prev
                                op <- Eng.disposeOutPt<'Z> op
                                if not (Eng.isValidClosedPath (smallTriangleTol, op)) then
                                    outrec.pts <- null'()
                                    invalidated <- true
                                else
                                    startOp <- op
                elif isNull' op then
                    loopOn <- false
                else
                    op <- op.next
                    if op === startOp then
                        loopOn <- false
            if not invalidated && isNotNull outrec.pts then
                fixSelfIntersects outrec


    let checkBounds (outrec: OutRec<'Z>) : bool =
        if isNull' outrec.pts then
            false
        elif not (Eng.isEmptyRect outrec) then
            true
        else
            cleanColinear outrec
            if isNull' outrec.pts || not (Eng.buildPath(outrec.pts, reverseSolution, false, outrec.path, coordEqTol, smallTriangleTol)) then
                false
            else
                Eng.getBounds(outrec)
                true


    let rec checkSplitOwner (outrec: OutRec<'Z>, splits: ResizeArray<int>) : bool =
        let mutable result = false
        let mutable i = 0
        while (not result) && i < Rarr.len splits do
            let mutable split = Rarr.getIdx (Rarr.getIdx i splits) outrecList
            if isNull' split.pts && isNotNull split.splits &&
               checkSplitOwner(outrec, split.splits) then
                result <- true
            else
                split <- Eng.getRealOutRec split
                if isNull' split ||
                   split === outrec ||
                   split.recursiveSplit === outrec then
                    ()
                else
                    split.recursiveSplit <- outrec

                    if isNotNull split.splits && checkSplitOwner(outrec, split.splits) then
                        result <- true
                    elif not (checkBounds split) ||
                         not (Eng.containsRect(split, outrec)) ||
                         not (path1InsidePath2(outrec.pts, split.pts)) then
                        ()
                    else
                        if not (Eng.isValidOwner(outrec, split)) then
                            split.owner <- outrec.owner
                        outrec.owner <- split
                        result <- true
            i <- i + 1
        result

    let buildPaths (solutionClosed: Paths64<'Z>, solutionOpen: Paths64<'Z>) : unit =
        let outrecList = outrecList // avoiding access via 'this' in JS
        solutionClosed|> Rarr.clear
        if isNotNull solutionOpen then
            solutionOpen|> Rarr.clear

        // outrecList.length is not static here because
        // CleanColinear can indirectly add additional OutRec<'Z>
        let mutable i = 0
        while i < Rarr.len outrecList do
            let outrec = Rarr.getIdx i outrecList
            i <- i + 1
            if isNotNull outrec.pts then
                let path = Geo.emptyPath64<'Z> hasZValues
                if outrec.isOpen then
                    if Eng.buildPath(outrec.pts, reverseSolution, true, path, coordEqTol, smallTriangleTol) then
                        if isNotNull solutionOpen then
                            solutionOpen.Add(path)
                else
                    cleanColinear outrec
                    // closed paths should always return a Positive orientation
                    // except when ReverseSolution == true
                    if Eng.buildPath(outrec.pts, reverseSolution, false, path, coordEqTol, smallTriangleTol) then
                        solutionClosed.Add(path)


    let rec recursiveCheckOwners (outrec: OutRec<'Z>, polypath: PolyPath64<'Z>) : unit =
        if isNotNull outrec.polypath || Eng.isEmptyRect outrec then
            ()
        else
            let mutable breakLoop = false
            while (not breakLoop) && isNotNull outrec.owner do
                if isNotNull outrec.owner.splits && checkSplitOwner(outrec, outrec.owner.splits) then
                    breakLoop <- true
                elif isNotNull outrec.owner.pts && checkBounds outrec.owner && Eng.containsRect(outrec.owner, outrec) && path1InsidePath2(outrec.pts, outrec.owner.pts) then
                    breakLoop <- true
                else
                    outrec.owner <- outrec.owner.owner

            if isNotNull outrec.owner then
                if isNull' outrec.owner.polypath then
                    recursiveCheckOwners(outrec.owner, polypath)
                outrec.polypath <- outrec.owner.polypath.AddChild(outrec.path)
            else
                outrec.polypath <- polypath.AddChild(outrec.path)

    let buildTree (polytree: PolyPath64<'Z>, solutionOpen: Paths64<'Z>) : unit =
        let outrecList = outrecList // avoiding access via 'this' in JS
        polytree.ClearContent()
        if isNotNull solutionOpen then
            solutionOpen|> Rarr.clear

        // outrecList.length is not static here because
        // checkBounds below can indirectly add additional
        // OutRec<'Z> (via FixOutRecPts & CleanColinear)
        let mutable i = 0
        while i < Rarr.len outrecList do
            let outrec = Rarr.getIdx i outrecList
            i <- i + 1
            if isNotNull outrec.pts then
                if outrec.isOpen then
                    let openPath = Geo.emptyPath64<'Z> hasZValues
                    if Eng.buildPath(outrec.pts, reverseSolution, true, openPath, coordEqTol, smallTriangleTol) then
                        if isNotNull solutionOpen then
                            solutionOpen.Add(openPath)
                else
                    if checkBounds outrec then
                        recursiveCheckOwners(outrec, polytree)



    let addPathsToVertexList (paths: Paths64<'Z>, pathType: PathType, isOpen: bool) : unit =
        for i = 0 to paths |> Rarr.lastIdx do
            let path = Rarr.getIdx i paths
            if path.IsEmpty then
                // Guard before reading xys[0] below: on .NET an empty path would throw an
                // unhelpful index error, and under Fable xys[0] is `undefined`, silently
                // poisoning the whole clip with NaN vertices.
                raise (ArgumentException $"Clipper64.AddPaths: the path at index {i} is empty (0 points).")
            if path.HasZs then
                hasZValues <- true
            let xys = path.XYs
            let zso = path.Zs
            let mutable prevV: Vertex<'Z> = null'()

            // do v0, the first vertex, outside the loop to initialize prevV
            let x = Rarr.getIdx 0 xys
            let y = Rarr.getIdx 1 xys
            let z = match zso with | None -> null'() | Some zs -> Rarr.getIdx 0 zs
            let v0 : Vertex<'Z> = { x = x; y = y; z = z; next = null'(); prev = null'(); flags = VertexFlags.None }
            vertexList.Add(v0)
            prevV <- v0

            // do all others
            let len = Rarr.len xys
            let mutable j = 2
            while j < len do
                let x = Rarr.getIdx j xys
                let y = Rarr.getIdx (j + 1) xys
                let z = match zso with | None -> null'() | Some zs -> Rarr.getIdx (j / 2) zs
                if xyNotEqual(prevV.x, prevV.y, x, y) then  // skip duplicates when building vertex list
                    let currV = { x = x; y = y; z = z; next = null'(); prev = prevV; flags = VertexFlags.None }
                    vertexList.Add currV
                    prevV.next <- currV
                    prevV <- currV
                j <- j + 2


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
                            Eng.addToLocalMinimaList(v0, pathType, true, minimaList)
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
                                Eng.addToLocalMinimaList(prevV, pathType, isOpen, minimaList)
                            prevV <- currV
                            currV <- currV.next

                        if isOpen then
                            prevV.flags <- prevV.flags ||| VertexFlags.OpenEnd
                            if goingUp then
                                prevV.flags <- prevV.flags ||| VertexFlags.LocalMax
                            else
                                Eng.addToLocalMinimaList(prevV, pathType, isOpen, minimaList)
                        elif goingUp <> goingUp0 then
                            if goingUp0 then
                                Eng.addToLocalMinimaList(prevV, pathType, false, minimaList)
                            else
                                prevV.flags <- prevV.flags ||| VertexFlags.LocalMax

    // #endregion
    // #region Public interface


    // let logStats() =
    //     printfn $"Clipper64: vertices={vertexList.Count}, minima={minimaList.Count}"
    //     printfn $"Clipper64: scanlineHeapSet={scanlineHeapSet.Count}, scanlineArr={scanlineArr.Count}"
    //     printfn $"Clipper64: intersects={intersectList.Count}, outrecs={outrecList.Count}"
    //     printfn $"Clipper64: horzsegs={horzSegList.Count}, horzjoins={horzJoinList.Count}"



    member _.HasOpenPaths
        with get() : bool = hasOpenPaths

    /// The Clipper class's PreserveColinear property only is only relevant when clipping closed paths.
    /// Paths will sometimes contain consecutive colinear segments,
    /// where the shared vertex can be removed without altering path shape.
    /// Removing these vertices simplifies path definitions and is generally (but not always) preferred in clipping solutions.
    /// Nevertheless, where consecutive colinear segments create 180 degree 'spikes', these will always be removed from closed solutions.
    member _.PreserveColinear
        // TODO find out if are colinear vertices are really always removed in open paths. ?? update dox string accordingly
        with get() : bool = preserveColinear
        and set(v: bool) : unit = preserveColinear <- v

    /// <summary>
    /// Size at which the engine switches its pending-scanline container from a small unsorted
    /// array (linear scan per insert/pop) to a max-heap plus hash-set (O(log n) operations).
    /// </summary>
    /// <remarks>
    /// Performance tuning only — it never changes clipping results. The array container is
    /// chosen at the start of each Execute when the local-minima count is at most this
    /// threshold, and is upgraded to the heap mid-sweep if the number of pending scanlines
    /// outgrows the threshold. Set 0 to always use the heap+set, or a very large value to
    /// always use the array. Default 64, settable process-wide for new instances via
    /// <c>Klipper.setDefaultScanlineArrayThreshold</c>. Per-instance setting.
    /// </remarks>
    member _.ScanlineArrayThreshold
        with get() : int = scanlineArrayThreshold
        and set(v: int) : unit =
            if v >= 0 then
                scanlineArrayThreshold <- v
            else
                invalidArg "ScanlineArrayThreshold" $"Scanline array threshold must be 0 or more. Got {v}."

    /// <summary>
    /// Maximum <b>perpendicular</b> distance from a candidate join point to a neighbouring
    /// edge line for the two edges to be joined in the adjacent-edge join checks.
    /// </summary>
    /// <remarks>
    /// Absolute distance, in coordinate units (the property is the square root of the
    /// internally stored squared tolerance); does not auto-scale.
    /// Raise this when touching contours fail to merge into a single contour. It is the main
    /// knob for <b>near-vertical / sloped</b> touching seams: those merge only via the
    /// adjacent-edge join, whereas near-horizontal seams have a separate join pass
    /// (<c>convertHorzSegsToJoins</c>) and so tolerate larger noise without tuning. A shared
    /// seam whose two sides are off by a gap <c>g</c> needs roughly <c>MergeVertexTolerance &gt; g</c>
    /// (e.g. a 1e-4 X deviation needs ~2e-4); the default 1e-5 only covers genuine float noise.
    /// A join requires this perpendicular gate <b>and</b> the angular
    /// <see cref="ColinearityTolerance"/> gate to pass, and is additionally suppressed by the
    /// near-top guard (<see cref="NearTopYToleranceFactor"/> / <see cref="NearTopYToleranceCap"/>);
    /// a wide value here does nothing on its own if those other gates reject the join.
    /// Measures a perpendicular-to-line distance, which is a different quantity from
    /// <see cref="CoordEqTolerance"/>'s point coincidence, so the two are independent.
    /// Default 1e-5 (the <see cref="CoordEqTolerance"/> default). Valid range 0.0 .. 1e9. Per-instance setting.
    /// </remarks>
    member _.MergeVertexTolerance
        with get() : float =
            mergeVertexToleranceSqrd |> Math.Sqrt
        and set(v: float) : unit =
            if v >= 0.0 && v <= 1e9 then
                mergeVertexToleranceSqrd <- v * v
            else
                invalidArg "MergeVertexTolerance" $"Merge vertex tolerance must be between 0.0 and 1e9. Got {v}."


    /// <summary>
    /// Coordinate-equality tolerance: two coordinates are treated as "the same point" when
    /// <c>abs (a - b) &lt;= CoordEqTolerance</c>.
    /// </summary>
    /// <remarks>
    /// Absolute distance, in coordinate units, applied per ordinate; does not auto-scale.
    /// Because intersection coordinates are computed as raw floats (not snapped to an integer
    /// grid), this absorbs floating-point rounding noise without fusing genuinely-distinct
    /// points. It backs the in-sweep point-equality and same-X / same-Y tests.
    /// Conceptually related to, but mechanically distinct from, <see cref="ColinearityTolerance"/>:
    /// this fuses near-duplicate <i>vertices</i> (a distance), whereas ColinearityTolerance
    /// flattens near-straight <i>runs</i> (an angle). It is also the fine, in-sweep counterpart
    /// to the coarse pre-pass <c>Klip.Snap.xAndY</c>, and is independent of
    /// <see cref="MergeVertexTolerance"/> (point coincidence vs perpendicular-to-line distance).
    /// Raise it to tolerate noisier near-duplicate input points.
    /// Default 1e-5. Valid range 0.0 .. 1e9. Per-instance setting: there is no module-global
    /// coordinate-equality tolerance — every coincidence test (including the <c>Geo.pointInPolygon</c>
    /// containment checks used by the sweep) takes this value as an explicit argument.
    /// </remarks>
    member _.CoordEqTolerance
        with get() : float = coordEqTol
        and set(v: float) : unit =
            if v >= 0.0 && v <= 1e9 then
                coordEqTol <- v
            else
                invalidArg "CoordEqTolerance" $"Coord equality tolerance must be between 0.0 and 1e9. Got {v}."

    /// <summary>
    /// Edge-height-relative factor of the near-top join guard. The guard suppresses an
    /// adjacent-edge join when the candidate join point sits at, or just below (in scanline-Y
    /// terms), an edge's top vertex — because a join that close to a maximum is almost always
    /// a maximum being processed, not a genuine touching-edge merge.
    /// </summary>
    /// <remarks>
    /// The guard window for an edge is <c>min(NearTopYToleranceCap, edgeHeight * NearTopYToleranceFactor)</c>.
    /// This factor scales the window with edge height (dimensionless, a fraction of edge height),
    /// so taller edges get a proportionally larger margin; <see cref="NearTopYToleranceCap"/>
    /// then caps it in absolute terms. Lowering it toward 0 effectively disables the guard
    /// (joins are allowed right up to the top vertex); raising it makes the engine more
    /// conservative about joining near edge tops.
    /// Default 1e-4. Valid range 0.0 .. 1.0. Per-instance setting.
    /// </remarks>
    member _.NearTopYToleranceFactor
        with get() : float = nearTopYToleranceFactor
        and set(v: float) : unit =
            if v >= 0.0 && v <= 1.0 then
                nearTopYToleranceFactor <- v
            else
                invalidArg "NearTopYToleranceFactor" $"Near-top Y tolerance factor must be between 0.0 and 1.0. Got {v}."

    /// <summary>
    /// Absolute ceiling, in coordinate units, of the near-top join guard window (see
    /// <see cref="NearTopYToleranceFactor"/> for what the guard does).
    /// </summary>
    /// <remarks>
    /// The guard window for an edge is <c>min(NearTopYToleranceCap, edgeHeight * NearTopYToleranceFactor)</c>,
    /// so this caps how wide the edge-height-relative window may grow for very tall edges.
    /// Being an absolute value, it does not auto-scale: at very large coordinate magnitudes a
    /// fixed cap is relatively tiny, while at sub-unit magnitudes the height-relative term
    /// dominates — a reason to normalize coordinate magnitude before clipping.
    /// Setting it to 0 disables the absolute window entirely (the guard then only triggers
    /// strictly above the top vertex).
    /// Default 2.0. Valid range 0.0 .. 1e9. Per-instance setting.
    /// </remarks>
    member _.NearTopYToleranceCap
        with get() : float = nearTopYToleranceCap
        and set(v: float) : unit =
            if v >= 0.0 && v <= 1e9 then
                nearTopYToleranceCap <- v
            else
                invalidArg "NearTopYToleranceCap" $"Near-top Y tolerance cap must be between 0.0 and 1e9. Got {v}."

    /// <summary>
    /// Window (absolute, in coordinate units) below which a 3-point solution ring is culled as a
    /// sliver triangle: a triangle is dropped when any two of its vertices are closer than this
    /// in both X and Y.
    /// </summary>
    /// <remarks>
    /// Inherited from integer-grid Clipper2, where the literal <c>2.0</c> meant two grid units.
    /// Being an absolute distance it does not auto-scale: like <see cref="NearTopYToleranceCap"/>
    /// it should be scaled to the coordinate magnitude of the input (a triangle spanning 1.9
    /// units is noise at coordinate magnitude ~1e6, but real geometry at sub-unit magnitudes).
    /// Set it to 0 to keep all triangles.
    /// Default 2.0. Valid range 0.0 .. 1e9. Per-instance setting: carried as an explicit
    /// argument into <c>buildPath</c> / <c>isValidClosedPath</c>, with no module-global.
    /// </remarks>
    member _.SmallTriangleTolerance
        with get() : float = smallTriangleTol
        and set(v: float) : unit =
            if v >= 0.0 && v <= 1e9 then
                smallTriangleTol <- v
            else
                invalidArg "SmallTriangleTolerance" $"Small triangle tolerance must be between 0.0 and 1e9. Got {v}."

    /// <summary>
    /// Area window (absolute, in squared coordinate units) used when a self-intersecting ring is
    /// split in two: the ring is discarded entirely when its area falls below this value, and the
    /// split-off triangle is kept only when its area exceeds half this value.
    /// </summary>
    /// <remarks>
    /// Inherited from integer-grid Clipper2 (which discards below area 1 and keeps splits above
    /// area 0.5; this port has historically used twice that). Being an absolute <i>area</i> it
    /// scales with the <b>square</b> of the coordinate magnitude, so when adjusting tolerances to
    /// input scale use ~M² rather than ~M (where M is the max absolute coordinate).
    /// Set it to 0 to keep all rings and splits regardless of area.
    /// Default 2.0. Valid range 0.0 .. 1e18. Per-instance setting.
    /// </remarks>
    member _.SplitAreaTolerance
        with get() : float = splitAreaTol
        and set(v: float) : unit =
            if v >= 0.0 && v <= 1e18 then
                splitAreaTol <- v
            else
                invalidArg "SplitAreaTolerance" $"Split area tolerance must be between 0.0 and 1e18. Got {v}."


    /// <summary>
    /// Dimensionless tolerance for deciding whether a cross product is effectively zero, i.e.
    /// whether three points are colinear.
    /// </summary>
    /// <remarks>
    /// Three points are treated as colinear when <c>cross(U,V)^2 &lt;= tolerance^2 * |U|^2 * |V|^2</c>,
    /// which is equivalent to <c>abs(sin(theta)) &lt;= tolerance</c>, where theta is the turn
    /// angle between the two edge vectors — so this is effectively an <i>angle</i> tolerance and,
    /// unlike the distance tolerances, it is scale-independent.
    /// Smaller values require straighter edges; larger values merge or clean more
    /// nearly-colinear vertices but may hide very narrow angles. This also lets colinear
    /// cleanup detect and close nearly 180-degree U-turn spike vertices.
    /// It affects cross-product signs, point-on-edge tests, active-edge ordering tie breaks,
    /// the angular gate of adjacent-edge joins, and colinear cleanup. Conceptually the angular
    /// partner of <see cref="CoordEqTolerance"/> (which is a distance).
    /// Raise it to flatten near-straight edges that otherwise leave stray micro-vertices.
    /// Default 1e-3 (0.5 in Clipper2). Valid range 1e-16 .. 1e6. Per-instance setting:
    /// carried as an explicit argument into the colinearity primitives, with no module-global.
    /// </remarks>
    member _.ColinearityTolerance
        with get() : float =
            Math.Sqrt colinTolSqrd // 0.25 as squared literal in Clipper2
        and set(v: float) : unit =
            if v >= 1e-16 && v <= 1e6 then
                colinTolSqrd <- v * v
            else
                invalidArg "ColinearityTolerance" $"Colinearity tolerance must be between 1e-16 and 1e6. Got {v}."

    /// <summary>
    /// Dimensionless tolerance for deciding whether an edge is horizontal: an edge counts as
    /// horizontal when <c>abs(Δy) &lt;= HorizontalAngleTolerance * abs(Δx)</c>.
    /// </summary>
    /// <remarks>
    /// This is effectively a slope (sin θ-like) angle from the horizontal, so — like
    /// <see cref="ColinearityTolerance"/> — it is scale-independent. Unrounded inputs can leave a
    /// shared near-horizontal edge a hair off exact (e.g. a top edge at 37 vs 37.00000000000001);
    /// the former exact <c>topY = botY</c> test then landed its two ends on distinct scanlines and
    /// could seal an open notch into a phantom hole. It backs both <c>getDx</c> (the ±infinity
    /// horizontal-edge encoding) and the in-sweep <c>isHorizontal</c> test, which stay coupled.
    /// Set it to 0 to restore the original exact behaviour; raise it to treat shallower edges as
    /// horizontal (too large will flatten genuine slopes and corrupt the sweep).
    /// Default 1e-6. Valid range 0.0 .. 1e-3. Per-instance setting, like
    /// <see cref="CoordEqTolerance"/> and <see cref="ColinearityTolerance"/>: carried as an
    /// explicit argument into the horizontal-edge primitives, with no module-global.
    /// </remarks>
    member _.HorizontalAngleTolerance
        with get() : float = horzAngleTol
        and set(v: float) : unit =
            if v >= 0.0 && v <= 1e-3 then
                horzAngleTol <- v
            else
                invalidArg "HorizontalAngleTolerance" $"Horizontal angle tolerance must be between 0.0 and 1e-3. Got {v}."



    /// Clipping operations will always return Positive oriented solutions as outer path.
    /// Inner hole contours will always have a Negative orientation
    /// (unless the Clipper object's ReverseSolution property has been enabled).
    /// This means that outer polygon contours will wind anti-clockwise when Y is positive upwards,
    /// and inner hole contours will wind clockwise. And because paths in clipping solutions never intersect,
    /// both EvenOdd and NonZero filling would correctly apply to the solution,
    /// though it's usual to apply the same FillRule that was applied to the subject and clip paths during clipping.
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
    member _.ZCallback
        with get() : ZCallback64<'Z> option = zCallback
        and set(v: ZCallback64<'Z> option) : unit = zCallback <- v


    member _.ClearAll() : unit =
        clearSolutionOnly()
        minimaList|> Rarr.clear
        vertexList|> Rarr.clear
        currentLocMin <- 0
        isSortedMinimaList <- false
        hasOpenPaths <- false
        hasZValues <- false


    member _.AddPaths(paths: Paths64<'Z>, pathType: PathType, [<OPT; DEF(false)>] isOpen: bool) : unit =
        if isNull' paths then
            invalidArg "paths" "Paths cannot be null."
        if isOpen && pathType = PathType.Clip then
            invalidArg "isOpen" "Clip paths cannot be open. Use AddOpenSubject for open subject paths."
        if isOpen then
            hasOpenPaths <- true
        isSortedMinimaList <- false
        addPathsToVertexList(paths, pathType, isOpen)


    member this.AddPath(path: Path64<'Z>, pathType: PathType, [<OPT; DEF(false)>] isOpen: bool) : unit =
        if isNull' path then
            invalidArg "path" "Path cannot be null."
        let tmp = Paths64<'Z>()
        tmp.Add(path)
        this.AddPaths(tmp, pathType, isOpen)

    /// Assumes the paths are closed and adds them as subjects.
    /// Just calls this.AddPaths with PathType.Subject and isOpen = false
    member this.AddSubject(paths: Paths64<'Z>) : unit =
        this.AddPaths(paths, PathType.Subject, isOpen = false)

    /// Assumes the paths are open and adds them as subjects.
    /// Just calls this.AddPaths with PathType.Subject and isOpen = true
    member this.AddOpenSubject(paths: Paths64<'Z>) : unit =
        this.AddPaths(paths, PathType.Subject, isOpen = true)

    /// Assumes the paths are closed and adds them as clips.
    /// Clip paths must be closed, but subject paths can be open or closed.
    /// Just calls this.AddPaths with PathType.Clip and isOpen = false
    member this.AddClip(paths: Paths64<'Z>) : unit =
        this.AddPaths(paths, PathType.Clip, isOpen = false)


    /// Executes the clipping operation.
    /// All Subject and Clip paths must have been added before calling Execute.
    /// You can call Execute as many times as you like with different ClipType and/or FillRule values.
    /// The input paths persist between calls.
    /// Returns a tuple of closed and open paths, in that order.
    /// solutionOpen may be null if no open paths were added.
    /// Raises an InvalidOperationException if the clipping operation fails (e.g. on malformed input).
    /// The result outer path contours will have a positive orientation
    /// (unless the Clipper object's ReverseSolution property has been enabled).
    member _.Execute(clipType: ClipType, fillRule: FillRule) : Paths64<'Z> * Paths64<'Z> =
        usingPolytree <- false
        executeInternal(clipType, fillRule)
        let solutionClosed = Paths64<'Z>()
        let solutionOpen = if hasOpenPaths then Paths64<'Z>() else null
        buildPaths(solutionClosed, solutionOpen)
        // logStats()
        clearSolutionOnly()
        solutionClosed, solutionOpen


    /// Executes the clipping operation .
    /// A PolyTree64 will never contain open paths since open paths can't contain paths.
    /// When clipping open paths, these will always be represented in solutions via a separate Paths64 structure.
    /// Returns a PolyTree64 representing the solution, where each node contains a path and references to its parent and child nodes.
    /// All Subject and Clip paths must have been added before calling Execute.
    /// You can call Execute as many times as you like with different ClipType and/or FillRule values.
    /// The input paths persist between calls.
    /// Returns a tuple of the PolyTree64 and the open paths.
    /// The open-path result is null when no open subjects were added.
    /// Raises an InvalidOperationException if the clipping operation fails (e.g. on malformed input).
    /// The result outer path contours will have a positive orientation
    /// (unless the Clipper object's ReverseSolution property has been enabled).
    member _.ExecutePolyTree(clipType: ClipType, fillRule: FillRule) : PolyTree64<'Z> * Paths64<'Z> = // using DefaultArg(null) would fail in Fable TS build
        usingPolytree <- true
        executeInternal(clipType, fillRule)
        let treeClosed = PolyTree64<'Z>()
        let treeOpen = if hasOpenPaths then Paths64<'Z>() else null
        buildTree(treeClosed, treeOpen)
        clearSolutionOnly()
        treeClosed, treeOpen
