
namespace Klip

open System


/// <summary>
/// Standalone, per-axis input pre-snapping for <see cref="T:Klip.Paths64`1"/>.
/// Collects the coordinates of near-vertical and near-horizontal segment runs - consecutive
/// vertices within a path whose X (or Y) values differ by no more than the tolerance,
/// including across the closing last-to-first segment - then clusters the collected X and Y
/// values independently per axis and snaps every coordinate in a cluster to the cluster mean,
/// mutating the path coordinate buffers <b>in place</b>.
/// </summary>
/// <remarks>
/// Only coordinates that are part of such a run are snapped: a vertex whose own neighbours
/// differ by more than the tolerance in an axis keeps its coordinate on that axis, even if it
/// is near-coincident with a vertex of another path.
/// Intended as a coarse preprocessing pass run <b>before</b> handing paths to
/// <see cref="T:Klip.Clipper64`1"/>. It is the coarse, axis-aligned counterpart to the fine,
/// in-sweep <c>CoordEqTolerance</c>: it exists mainly to fuse vertices that differ only by
/// float noise on what should be a shared horizontal or vertical edge, preventing touching
/// contours from resolving into phantom holes.
/// Because the snap is per-axis (not Euclidean), two points 1e-8 apart in X but far apart in
/// Y get only their X's merged.
/// The tolerance is an absolute distance in coordinate units and does not auto-scale, so
/// normalize coordinate magnitude first. The sane ordering is
/// <c>CoordEqTolerance ≤ tolerance</c>: setting it below <c>CoordEqTolerance</c> makes
/// snapping a near no-op (anything it would merge, the sweep already treats as equal); setting
/// it far above pre-collapses coordinates the sweep would otherwise keep distinct.
/// </remarks>
[<RequireQualifiedAccess>]
module Snap =

    [<NoComparison;NoEquality>]
    type private SnapX =
        {
        mutable x: float
        vs: ResizeArray<float>
        idx: int
        }

    [<NoComparison;NoEquality>]
    type private SnapY =
        {
        mutable y: float
        vs: ResizeArray<float>
        idx: int
        }

    /// Default per-axis clustering distance, used when no tolerance is supplied.
    /// See tests in .\Test\Rhino\unionAllScalesRh.fsx.
    [<Literal>]
    let DefaultTolerance : float = 1e-8

    #if !FABLE_COMPILER
    let private xSorter (a: SnapX) (b: SnapX) : int =
        if a.x < b.x then -1
        elif a.x > b.x then 1
        else 0

    let private ySorter (a: SnapY) (b: SnapY) : int =
        if a.y < b.y then -1
        elif a.y > b.y then 1
        else 0
    #endif

    let private sortX (xs: ResizeArray<SnapX>) : unit =
        #if FABLE_COMPILER
            Fable.Core.JsInterop.emitJsStatement (xs) "$0.sort((a, b) => a.x - b.x)"
        #else
            xs.Sort(xSorter)
        #endif

    let private sortY (ys: ResizeArray<SnapY>) : unit =
        #if FABLE_COMPILER
            Fable.Core.JsInterop.emitJsStatement (ys) "$0.sort((a, b) => a.y - b.y)"
        #else
            ys.Sort(ySorter)
        #endif

    /// <summary>
    /// Snaps the X and Y coordinates of every path across all the given path collections in
    /// place, treating them as one shared coordinate set (so a vertex in one collection can
    /// snap onto a near-coincident vertex in another, provided both are part of near-axis-aligned
    /// segment runs within their own paths - see the module remarks). Use this to snap subject
    /// and clip paths together before clipping.
    /// </summary>
    /// <param name="tolerance">Per-axis clustering distance (absolute coordinate units).</param>
    /// <param name="pathCollections">The path collections whose coordinate buffers are mutated in place.</param>
    let xAndY (tolerance: float) (pathCollections: seq<Paths64<'Z>>) : unit =
        let snapXList = ResizeArray<SnapX>()
        let snapYList = ResizeArray<SnapY>()

        // (1) first collect all vertical or horizontal segment runs, remembering original indices
        for paths in pathCollections do
            for j = 0 to paths |> Rarr.lastIdx do
                let xys = (Rarr.getIdx j paths).XYs
                let cnt = Rarr.len xys
                if cnt >= 2 then
                    let mutable xLastOK = true
                    let mutable yLastOK = true
                    let mutable prevX = Rarr.getIdx (cnt-2) xys // start with last vertex, so we can compare it to the first
                    let mutable prevY = Rarr.getIdx (cnt-1) xys
                    // check last with first:
                    // X:
                    let mutable x = Rarr.getIdx 0 xys
                    if abs(x - prevX) <= tolerance then
                        snapXList.Add { x = prevX; vs = xys; idx = cnt-2 }
                        snapXList.Add { x = x    ; vs = xys; idx = 0 }
                        xLastOK <- false
                    else
                        prevX <- x
                    //Y:
                    let mutable y = Rarr.getIdx 1 xys
                    if abs(y - prevY) <= tolerance then
                        snapYList.Add { y = prevY; vs = xys; idx = cnt-1 }
                        snapYList.Add { y = y    ; vs = xys; idx = 1 }
                        yLastOK <- false
                    else
                        prevY <- y
                    // loop rest:
                    let mutable k = 2
                    while k < cnt do
                        x <- Rarr.getIdx k xys
                        if abs(x - prevX) <= tolerance then
                            if xLastOK then // also add starting vertex of the run, but only once
                                snapXList.Add { x = prevX; vs = xys; idx = k-2 }
                                xLastOK <- false
                            snapXList.Add { x = x; vs = xys; idx = k }
                        else
                            prevX <- x
                            xLastOK <- true

                        y <- Rarr.getIdx (k + 1) xys
                        if abs(y - prevY) <= tolerance then
                            if yLastOK then // also add starting vertex of the run, but only once
                                snapYList.Add { y = prevY; vs = xys; idx = k-1 }
                                yLastOK <- false
                            snapYList.Add { y = y; vs = xys; idx = k+1 }
                        else
                            prevY <- y
                            yLastOK <- true

                        k <- k + 2

        // (2) sort the snap lists
        sortX snapXList
        sortY snapYList

        // (3.1) assign mean values to close coordinates
        let yLen = snapYList |> Rarr.len
        if yLen > 0 then
            let mutable prev = (Rarr.getIdx 0 snapYList).y
            let mutable mergeStartIdx = -1
            let mutable i = 1
            let mutable meanY = 0.0
            let inline flushYRun (mergeEndIdx: int) =
                if mergeStartIdx >= 0 then
                    let mean = meanY / float (mergeEndIdx - mergeStartIdx + 1)
                    for j = mergeStartIdx to mergeEndIdx do
                        (Rarr.getIdx j snapYList).y <- mean
                    meanY <- 0.0
                    mergeStartIdx <- -1

            while i < yLen do
                let curr = (Rarr.getIdx i snapYList).y
                if curr - prev <= tolerance then
                    meanY <- meanY + curr
                    if mergeStartIdx = -1 then
                        meanY <- meanY + prev
                        mergeStartIdx <- i - 1
                    // do not update prev here
                else
                    // we are at the end of a run of close coordinates, assign mean value to all in the run
                    flushYRun (i - 1)
                    prev <- curr
                i <- i + 1
            flushYRun (yLen - 1)

        // (3.2) repeat for X coordinates
        let xLen = snapXList |> Rarr.len
        if xLen > 0 then
            let mutable prev = (Rarr.getIdx 0 snapXList).x
            let mutable mergeStartIdx = -1
            let mutable i = 1
            let mutable meanX = 0.0
            let inline flushXRun (mergeEndIdx: int) =
                if mergeStartIdx >= 0 then
                    let mean = meanX / float (mergeEndIdx - mergeStartIdx + 1)
                    for j = mergeStartIdx to mergeEndIdx do
                        (Rarr.getIdx j snapXList).x <- mean
                    meanX <- 0.0
                    mergeStartIdx <- -1

            while i < xLen do
                let curr = (Rarr.getIdx i snapXList).x
                if curr - prev <= tolerance then
                    meanX <- meanX + curr
                    if mergeStartIdx = -1 then
                        meanX <- meanX + prev
                        mergeStartIdx <- i - 1
                    // do not update prev here
                else
                    // we are at the end of a run of close coordinates, assign mean value to all in the run
                    flushXRun (i - 1)
                    prev <- curr
                i <- i + 1
            flushXRun (xLen - 1)

        // (4) now replace the original coordinates in the input paths with the snapped coordinates
        for i = 0 to snapXList |> Rarr.lastIdx do
            let sx = Rarr.getIdx i snapXList
            sx.vs |> Rarr.setIdx sx.idx sx.x
        for i = 0 to snapYList |> Rarr.lastIdx do
            let sy = Rarr.getIdx i snapYList
            sy.vs |> Rarr.setIdx sy.idx sy.y

    /// <summary>
    /// Snaps the X and Y coordinates of every path in <paramref name="paths"/> in place.
    /// Convenience wrapper over <see cref="M:Klip.Snap.xAndY"/> for a single collection.
    /// </summary>
    /// <param name="tolerance">Per-axis clustering distance (absolute coordinate units).</param>
    /// <param name="paths">The paths whose coordinate buffers are mutated in place.</param>
    let xAndYSingle (tolerance: float) (paths: Paths64<'Z>) : unit =
        xAndY tolerance [| paths |]