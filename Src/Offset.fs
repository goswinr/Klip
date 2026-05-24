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
open Klip.Null
open Klip.KlipInternal


/// Specifies how 'corners' (joins) between offset segments are constructed.
type JoinType =
    /// Sharp corners; reverts to <c>Square</c> when the corner exceeds the miter limit.
    | Miter = 0
    /// Squared off corners.
    | Square = 1
    /// Beveled (chamfered) corners.
    | Bevel = 2
    /// Rounded corners.
    | Round = 3

/// Specifies whether subject paths are open or closed, and how their ends are capped.
type EndType =
    /// Treat subject paths as closed polygons.
    | Polygon = 0
    /// Treat subject paths as open, with ends joined together.
    | Joined = 1
    /// Open path with butt (perpendicular) ends.
    | Butt = 2
    /// Open path with squared ends extending past the line endpoints.
    | Square = 3
    /// Open path with rounded ends.
    | Round = 4


//#region OffsetInternal

module internal OffsetInternal =

    let inline isAlmostZero (v: float) : bool =
        abs v <= 1.0E-12

    let inline crossProductD (v1x: float) (v1y: float) (v2x: float) (v2y: float) : float =
        v1y * v2x - v2y * v1x

    let inline dotProductD (v1x: float) (v1y: float) (v2x: float) (v2y: float) : float =
        v1x * v2x + v1y * v2y

    /// Returns z value at index i, or DEFZ if the path has no Z values.
    let inline getZ (path: Path64<'Z>) (i: int) : 'Z =
        match path.Zs with
        | Some zs -> zs[i]
        | None -> Null.DEFZ()

    // inlined now:

    // Returns the unit normal of the edge from (pt1X, pt1Y) to (pt2X, pt2Y) as (nx, ny).
    // let getUnitNormal (pt1X: float, pt1Y: float, pt2X: float, pt2Y: float) : float * float =
    //     let dx = pt2X - pt1X
    //     let dy = pt2Y - pt1Y
    //     if dx = 0.0 && dy = 0.0 then
    //         0.0, 0.0
    //     else
    //         let f = 1.0 / sqrt (dx * dx + dy * dy)
    //         dy * f, -dx * f

    // /// Find the index of the lowest path (and whether its area is negative).
    // let getLowestPathInfo (paths: Paths64<'Z>) : int * bool =
    //     let mutable idx = -1
    //     let mutable isNegArea = false
    //     let mutable botX = Double.MaxValue
    //     let mutable botY = Double.MinValue
    //     for i = 0 to paths.Count - 1 do
    //         let path = paths[i]
    //         let mutable a = Double.MaxValue
    //         let cnt = path.PointCount
    //         let mutable broken = false
    //         let mutable k = 0
    //         while not broken && k < cnt do
    //             let x = path.GetX(k)
    //             let y = path.GetY(k)
    //             if not (y < botY || y = botY && x >= botX) then
    //                 if a = Double.MaxValue then
    //                     a <- path.SignedArea
    //                     if a = 0.0 then
    //                         broken <- true
    //                     else
    //                         isNegArea <- a < 0.0
    //                 if not broken then
    //                     idx <- i
    //                     botX <- x
    //                     botY <- y
    //             k <- k + 1
    //     idx, isNegArea

    /// Removes consecutive duplicate vertices.
    /// For closed paths, also removes a trailing vertex equal to the first.
    let stripDuplicates (path: Path64<'Z>) (isClosedPath: bool) : Path64<'Z> =
        let cnt = path.PointCount
        let result = Geo.emptyPath64Sized<'Z> path.HasZs cnt
        if cnt = 0 then
            result
        else
            let firstX = path.GetX(0)
            let firstY = path.GetY(0)
            result.Add(firstX, firstY, getZ path 0)
            let mutable lastX = firstX
            let mutable lastY = firstY
            for i = 1 to cnt - 1 do
                let xi = path.GetX(i)
                let yi = path.GetY(i)
                if not (xi = lastX && yi = lastY) then
                    lastX <- xi
                    lastY <- yi
                    result.Add(lastX, lastY, getZ path i)
            if isClosedPath && result.PointCount > 0 then
                let lastIdx = result.PointCount - 1
                if result.GetX(lastIdx) = firstX && result.GetY(lastIdx) = firstY then
                    let xys = result.XYs
                    Rarr.pop xys
                    Rarr.pop xys
                    match result.Zs with
                    | Some zs -> Rarr.pop zs
                    | None -> ()
            result


    /// Build an ellipse path centered at (cx, cy) with the given radii and step count.
    let ellipse (cx: float, cy: float, radiusX: float, radiusY: float, steps: int, hasZ: bool, z: 'Z) : Path64<'Z> =
        let result = Geo.emptyPath64Sized<'Z> hasZ steps
        if radiusX <= 0.0 then
            result
        else
            let radiusY = if radiusY <= 0.0 then radiusX else radiusY
            let steps =
                if steps <= 2 then int (ceil (Math.PI * sqrt ((radiusX + radiusY) / 2.0)))
                else steps
            let si = sin (2.0 * Math.PI / float steps)
            let co = cos (2.0 * Math.PI / float steps)
            let mutable dx = co
            let mutable dy = si
            result.Add(Geo.jsRound (cx + radiusX), cy, z)
            for _ = 1 to steps - 1 do
                result.Add(Geo.jsRound (cx + radiusX * dx), Geo.jsRound (cy + radiusY * dy), z)
                let x = dx * co - dy * si
                dy <- dy * co + dx * si
                dx <- x
            result


    /// Reverses a path (returns a new Path64).
    let reversePath (path: Path64<'Z>) : Path64<'Z> =
        let cnt = path.PointCount
        let hasZs = path.HasZs
        let result = Geo.emptyPath64Sized<'Z> hasZs cnt
        let xys = path.XYs
        let resXYs = result.XYs
        if hasZs then
            let pathZs = path.Zs.Value
            let resZs = result.Zs.Value
            for i = cnt - 1 downto 0 do
                resXYs.Add(xys[i * 2])
                resXYs.Add(xys[i * 2 + 1])
                resZs.Add(pathZs[i])
        else
            for i = cnt - 1 downto 0 do
                resXYs.Add(xys[i * 2])
                resXYs.Add(xys[i * 2 + 1])
        result


    /// Internal Group type holding pre-processed input paths and derived flags.
    type Group<'Z>(paths: Paths64<'Z>, joinType: JoinType, endType: EndType) =
        let isJoined = endType = EndType.Polygon || endType = EndType.Joined
        let inPaths = Paths64<'Z>()

        let mutable lowestPathIdx = -1
        let mutable isNegArea = false

        do
            for path in paths do
                inPaths.Add(stripDuplicates path isJoined)

            // Find the index of the lowest path (and whether its area is negative).
            // inline of : let getLowestPathInfo (paths: Paths64<'Z>) : int * bool =
            if endType = EndType.Polygon then
                let mutable botX = Double.MaxValue
                let mutable botY = Double.MinValue
                for i = 0 to paths.Count - 1 do
                    let path = paths[i]
                    let mutable a = Double.MaxValue
                    let cnt = path.PointCount
                    let mutable broken = false
                    let mutable k = 0
                    while not broken && k < cnt do
                        let x = path.GetX(k)
                        let y = path.GetY(k)
                        if not (y < botY || y = botY && x >= botX) then
                            if a = Double.MaxValue then
                                a <- path.SignedArea
                                if a = 0.0 then
                                    broken <- true
                                else
                                    isNegArea <- a < 0.0
                            if not broken then
                                lowestPathIdx <- i
                                botX <- x
                                botY <- y
                        k <- k + 1

        // The lowermost path must be an outer path, so if its orientation is negative,
        // then flag that the whole group is 'reversed' (will negate delta etc.)
        // as this is much more efficient than reversing every path.
        let pathsReversed =
            lowestPathIdx >= 0 && isNegArea

        member _.InPaths = inPaths
        member _.JoinType = joinType
        member _.EndType = endType
        member _.PathsReversed = pathsReversed
        member _.LowestPathIdx = lowestPathIdx

//#endregion
//#region ClipperOffset

open OffsetInternal

type DeltaZCallback64<'Z> =
    Path64<'Z> * ResizeArray<float> * int * int -> float


/// Performs polygon offsetting (inflate / deflate / inset / outset) of paths.
/// After adding paths via <c>AddPath</c> / <c>AddPaths</c>, call <c>Execute(delta, ...)</c>:
/// positive <c>delta</c> values inflate the paths, negative values deflate them.
/// <param name="miterLimit">This property sets the maximum distance in multiples of delta that vertices can be offset from their original positions
///  before squaring is applied. (Squaring truncates a miter by 'cutting it off' at 1 × delta distance from the original vertex.)
/// The default value for MiterLimit is 2 (ie twice delta).
/// This is also the smallest MiterLimit that's allowed.
/// If mitering was unrestricted (ie without any squaring),
/// then offsets at very acute angles would generate unacceptably long 'spikes'.</param>
/// <param name="arcTolerance">ArcTolerance is only relevant when offsetting with JoinType.Round and / or EndType.Round
/// (see ClipperOffset.AddPath and ClipperOffset.AddPaths).
/// The Clipper2 library approximates arcs by using series of relatively short straight line segments (see Trigonometry).
/// And logically, shorter line segments will produce better arc approximations. But very short segments can degrade performance,
/// usually with little or no discernable improvement in curve quality.
/// Very short segments can even detract from curve quality, due to the effects of integer rounding.
/// Arc tolerance is user defined since there isn't an optimal number of line segments for any given arc radius
/// (ie that perfectly balances curve approximation with performance).
/// Nevertheless, when the user doesn't define an arc tolerance and uses the default value (0.0),
/// a 'default' arc tolerance is calculated (see below) that generally produces visually smooth arc approximations,
/// while avoiding excessively small segment lengths.
/// The default ArcTolerance is: offset_radius / 500.
/// When changing ArcTolerance to a value > 0,
/// it needs to be a sensible fraction of the offset delta for the reasons given above.</param>
/// <param name="preserveCollinear">Whether to preserve collinear points in the output. Default is false.</param>
/// <param name="reverseSolution">Whether to reverse the orientation of the output paths. Default is false.</param>
/// Note: when offsetting open paths with <c>EndType.Joined</c>, the provided paths are treated as closed for offsetting purposes, but the output paths are still open (ie the start and end vertices are not connected).
/// This allows for consistent joins at the start and end of the path, without producing a closed loop.
type ClipperOffset<'Z>( ?miterLimit: float, ?arcTolerance: float, ?preserveCollinear: bool, ?reverseSolution: bool ) =

    static let Tolerance = 1.0E-12

    // Clipper2 approximates arcs by using series of relatively short straight
    // line segments. And logically, shorter line segments will produce better arc
    // approximations. But very short segments can degrade performance, usually
    // with little or no discernable improvement in curve quality. Very short
    // segments can even detract from curve quality, due to the effects of integer
    // rounding. Since there isn't an optimal number of line segments for any given
    // arc radius (that perfectly balances curve approximation with performance),
    // arc tolerance is user defined. Nevertheless, when the user doesn't define
    // an arc tolerance (ie leaves alone the 0 default value), the calculated
    // default arc tolerance (offset_radius / 500) generally produces good (smooth)
    // arc approximations without producing excessively small segment lengths.
    // See also: https://www.angusj.com/clipper2/Docs/Trigonometry.htm
    static let arc_const = 0.002 // 1/500

    let groupList = ResizeArray<Group<'Z>>()
    let mutable pathOut: Path64<'Z> = null'()
    // Normals stored as a flat ResizeArray<float> with X, Y interleaved.
    let normals = ResizeArray<float>()
    let mutable solution: Paths64<'Z> = null'()
    let mutable solutionTree: PolyTree64<'Z> = null'()

    let mutable groupDelta = 0.0 // *0.5 for open paths; *-1.0 for negative areas
    let mutable delta = 0.0
    let mutable mitLimSqr = 0.0
    let mutable stepsPerRad = 0.0
    let mutable stepSin = 0.0
    let mutable stepCos = 0.0
    let mutable joinType = JoinType.Bevel
    let mutable endType = EndType.Polygon

    let mutable arcTolerance = defaultArg arcTolerance 0.0
    let mutable mergeGroups = true
    let mutable miterLimit = defaultArg miterLimit 2.0
    let mutable preserveCollinear = defaultArg preserveCollinear false
    let mutable reverseSolution = defaultArg reverseSolution false
    let mutable zCallback: ZCallback64<'Z> option = None
    let mutable deltaCallback: DeltaZCallback64<'Z> option = None
    let mutable hasZValues = false

    let normX (i: int) : float = normals[i * 2]
    let normY (i: int) : float = normals[i * 2 + 1]

    let buildNormals (path: Path64<'Z>) : unit =
        let cnt = path.PointCount
        normals|> Rarr.clear
        if cnt > 0 then
            let xys = path.XYs
            let mutable pt1X = xys[0]
            let mutable pt1Y = xys[1]
            for i = 1 to cnt - 1 do
                let pt2X = xys[i * 2]
                let pt2Y = xys[i * 2 + 1]
                let dx = pt2X - pt1X
                let dy = pt2Y - pt1Y
                if dx = 0.0 && dy = 0.0 then
                    normals.Add(0.0)
                    normals.Add(0.0)
                else
                    let f = 1.0 / sqrt (dx * dx + dy * dy)
                    normals.Add( dy * f)
                    normals.Add(-dx * f) // x and y in reverse order, x negative, to build normals
                pt1X <- pt2X
                pt1Y <- pt2Y

            // do last point to first point to close the path
            let pt2X = xys[0]
            let pt2Y = xys[1]
            let dx = pt2X - pt1X
            let dy = pt2Y - pt1Y
            if dx = 0.0 && dy = 0.0 then
                normals.Add(0.0)
                normals.Add(0.0)
            else
                let f = 1.0 / sqrt (dx * dx + dy * dy)
                normals.Add( dy * f)
                normals.Add(-dx * f) // x and y in reverse order, x negative, to build normals




    let getPerpendicX (path: Path64<'Z>) (j: int) (normIdx: int) : float  =
        path.GetX(j) + normX normIdx * groupDelta

    let getPerpendicY (path: Path64<'Z>) (j: int) (normIdx: int) : float =
        path.GetY(j) + normY normIdx * groupDelta




    let doBevel (path: Path64<'Z>) (j: int) (k: int) : unit =
        let pjz = getZ path j
        let px = path.GetX(j)
        let py = path.GetY(j)
        if j = k then
            let absDelta = abs groupDelta
            let nxJ = normX j
            let nyJ = normY j
            pathOut.Add(Geo.jsRound (px - absDelta * nxJ), Geo.jsRound (py - absDelta * nyJ), pjz)
            pathOut.Add(Geo.jsRound (px + absDelta * nxJ), Geo.jsRound (py + absDelta * nyJ), pjz)
        else
            pathOut.Add(Geo.jsRound (px + groupDelta * normX k), Geo.jsRound (py + groupDelta * normY k), pjz)
            pathOut.Add(Geo.jsRound (px + groupDelta * normX j), Geo.jsRound (py + groupDelta * normY j), pjz)


    /// Intersection of two infinite 2D lines. Mirrors TS ClipperOffset.intersectPoint.
    let intersectPointXY (p1aX: float, p1aY: float, p1bX: float, p1bY: float, p2aX: float, p2aY: float, p2bX: float, p2bY: float) : float * float =
        if isAlmostZero (p1aX - p1bX) then
            if isAlmostZero (p2aX - p2bX) then
                0.0, 0.0
            else
                let m2 = (p2bY - p2aY) / (p2bX - p2aX)
                let b2 = p2aY - m2 * p2aX
                p1aX, m2 * p1aX + b2
        elif isAlmostZero (p2aX - p2bX) then
            let m1 = (p1bY - p1aY) / (p1bX - p1aX)
            let b1 = p1aY - m1 * p1aX
            p2aX, m1 * p2aX + b1
        else
            let m1 = (p1bY - p1aY) / (p1bX - p1aX)
            let b1 = p1aY - m1 * p1aX
            let m2 = (p2bY - p2aY) / (p2bX - p2aX)
            let b2 = p2aY - m2 * p2aX
            if isAlmostZero (m1 - m2) then
                0.0, 0.0
            else
                let x = (b2 - b1) / (m1 - m2)
                x, m1 * x + b1

    // TODO inline the above to avoid tuple allocation?


    let doSquare (path: Path64<'Z>) (j: int) (k: int) : unit =
        let pjz = getZ path j
        let px = path.GetX(j)
        let py = path.GetY(j)
        // Compute average tangent unit vector (vx, vy) reflecting incoming/outgoing normals.
        let vx, vy =
            if j = k then
                normY j, -(normX j)
            else
                // average of (-norm[k].y, norm[k].x) and (norm[j].y, -norm[j].x)
                let ax = -(normY k) + normY j
                let ay = normX k + -(normX j)
                let h = sqrt (ax * ax + ay * ay)
                if h < 0.001 then 0.0, 0.0
                else ax / h, ay / h
        let absDelta = abs groupDelta
        // Offset original vertex absDelta units along the tangent.
        let qx = px + absDelta * vx
        let qy = py + absDelta * vy
        // Perpendicular vertices to ptQ.
        let pt1x, pt1y = qx + groupDelta * vy, qy + groupDelta * (-vx)
        let pt2x, pt2y = qx + groupDelta * (-vy), qy + groupDelta * vx
        // Two vertices along one edge offset.
        let pt3x = getPerpendicX path k k
        let pt3y = getPerpendicY path k k
        if j = k then
            let pt4x = pt3x + vx * groupDelta
            let pt4y = pt3y + vy * groupDelta
            let ix, iy = intersectPointXY (pt1x, pt1y, pt2x, pt2y, pt3x, pt3y, pt4x, pt4y)
            // Reflect through Q to get the second intersect point.
            let rx = qx + (qx - ix)
            let ry = qy + (qy - iy)
            pathOut.Add(Geo.jsRound rx, Geo.jsRound ry, pjz)
            pathOut.Add(Geo.jsRound ix, Geo.jsRound iy, pjz)
        else
            let pt4x = getPerpendicX path j k
            let pt4y = getPerpendicY path j k
            let ix, iy = intersectPointXY (pt1x, pt1y, pt2x, pt2y, pt3x, pt3y, pt4x, pt4y)
            pathOut.Add(Geo.jsRound ix, Geo.jsRound iy, pjz)
            let rx = qx + (qx - ix)
            let ry = qy + (qy - iy)
            pathOut.Add(Geo.jsRound rx, Geo.jsRound ry, pjz)


    let doMiter (path: Path64<'Z>) (j: int) (k: int) (cosA: float) : unit =
        let q = groupDelta / (cosA + 1.0)
        pathOut.Add(
            Geo.jsRound (path.GetX(j) + (normX k + normX j) * q),
            Geo.jsRound (path.GetY(j) + (normY k + normY j) * q),
            getZ path j)


    let doRound (path: Path64<'Z>) (j: int) (k: int) (angle: float) : unit =
        // When deltaCallback is assigned, groupDelta won't be constant,
        // so we recalculate per vertex.
        if deltaCallback.IsSome then
            let absDelta = abs groupDelta
            let arcTol = if arcTolerance > 0.01 then arcTolerance else absDelta * arc_const
            let stepsPer360 = Math.PI / acos (1.0 - arcTol / absDelta)
            stepSin <- sin (2.0 * Math.PI / stepsPer360)
            stepCos <- cos (2.0 * Math.PI / stepsPer360)
            if groupDelta < 0.0 then stepSin <- -stepSin
            stepsPerRad <- stepsPer360 / (2.0 * Math.PI)

        let ptx = path.GetX(j)
        let pty = path.GetY(j)
        let ptz = getZ path j
        let mutable offX = normX k * groupDelta
        let mutable offY = normY k * groupDelta
        if j = k then
            offX <- -offX
            offY <- -offY

        pathOut.Add(Geo.jsRound (ptx + offX), Geo.jsRound (pty + offY), ptz)
        let steps = int (ceil (stepsPerRad * abs angle))
        for _ = 1 to steps - 1 do
            let nx = offX * stepCos - stepSin * offY
            let ny = offX * stepSin + offY * stepCos
            offX <- nx
            offY <- ny
            pathOut.Add(Geo.jsRound (ptx + offX), Geo.jsRound (pty + offY), ptz)
        // Final perpendic at j using normals[j].
        pathOut.Add(
            Geo.jsRound (ptx + normX j * groupDelta),
            Geo.jsRound (pty + normY j * groupDelta),
            ptz)


    let offsetPoint (group: Group<'Z>) (path: Path64<'Z>) (j: int) (k: int) : unit =
        let pjx = path.GetX(j)
        let pjy = path.GetY(j)
        let pkx = path.GetX(k)
        let pky = path.GetY(k)
        if pjx = pkx && pjy = pky then
            ()
        else
            // A is the change in angle where edges join.
            // A == 0 : flat join; A == PI : edges 'spike'.
            // sin(A) < 0 : right turning ; cos(A) < 0 : change > 90 degrees.
            let nXj = normX j
            let nYj = normY j
            let nXk = normX k
            let nYk = normY k
            let mutable sinA = crossProductD nXj nYj nXk nYk
            let cosA = dotProductD nXj nYj nXk nYk
            if sinA > 1.0 then
                sinA <- 1.0
            elif sinA < -1.0 then
                sinA <- -1.0

            match deltaCallback with
            | Some cb ->
                groupDelta <- cb (path, normals, j, k)
                if group.PathsReversed then groupDelta <- -groupDelta
            | None -> ()

            if abs groupDelta < Tolerance then
                pathOut.Add(pjx, pjy, getZ path j)
            else
                if cosA > -0.999 && (sinA * groupDelta < 0.0) then
                    // concave (#593) - insert 3 points to produce negative regions
                    // which the union finishing pass will remove. Best way to ensure
                    // path reversals (over-shrunk paths) are removed.
                    let pjz = getZ path j
                    let x1 = getPerpendicX path j k
                    let y1 = getPerpendicY path j k
                    pathOut.Add(Geo.jsRound x1, Geo.jsRound y1, pjz)
                    pathOut.Add(pjx, pjy, pjz) // (#405, #873, #916)
                    let x2 = getPerpendicX path j j
                    let y2 = getPerpendicY path j j
                    pathOut.Add(Geo.jsRound x2, Geo.jsRound y2, pjz)
                elif cosA > 0.999 && joinType <> JoinType.Round then
                    // almost straight - less than 2.5 degrees (#424, #482, #526 & #724)
                    doMiter path j k cosA
                else
                    match joinType with
                    | JoinType.Miter ->
                        if cosA > mitLimSqr - 1.0 then
                            doMiter path j k cosA
                        else
                            doSquare path j k
                    | JoinType.Round ->
                        doRound path j k (atan2 sinA cosA)
                    | JoinType.Bevel ->
                        doBevel path j k
                    | _ ->
                        doSquare path j k


    let offsetPolygon (group: Group<'Z>) (path: Path64<'Z>) : unit =
        pathOut <- Geo.emptyPath64Sized<'Z> hasZValues (path.PointCount * 2)
        let cnt = path.PointCount
        let mutable prev = cnt - 1
        for i = 0 to cnt - 1 do
            offsetPoint group path i prev
            prev <- i
        solution.Add(pathOut)


    let offsetOpenJoined (group: Group<'Z>) (path: Path64<'Z>) : unit =
        offsetPolygon group path
        let rev = reversePath path
        buildNormals rev
        offsetPolygon group rev


    let offsetOpenPath (group: Group<'Z>) (path: Path64<'Z>) : unit =
        pathOut <- Geo.emptyPath64Sized<'Z> hasZValues (path.PointCount * 2)
        let highI = path.PointCount - 1

        match deltaCallback with
        | Some cb -> groupDelta <- cb (path, normals, 0, 0)
        | None -> ()

        // Line start cap.
        if abs groupDelta < Tolerance then
            pathOut.Add(path.GetX(0), path.GetY(0), getZ path 0)
        else
            match endType with
            | EndType.Butt -> doBevel path 0 0
            | EndType.Round -> doRound path 0 0 Math.PI
            | _ -> doSquare path 0 0

        // Offset the left side going forward.
        let mutable k = 0
        for i = 1 to highI - 1 do
            offsetPoint group path i k
            k <- i

        // Reverse normals.
        for i = highI downto 1 do
            normals[i * 2]     <- -(normals[(i - 1) * 2])
            normals[i * 2 + 1] <- -(normals[(i - 1) * 2 + 1])
        // After the above, normals[highI] holds the reversed normals[highI - 1].
        // Copy normals[highI] into normals[0] to match the TS implementation.
        normals[0] <- normals[highI * 2]
        normals[1] <- normals[highI * 2 + 1]

        match deltaCallback with
        | Some cb -> groupDelta <- cb (path, normals, highI, highI)
        | None -> ()

        // Line end cap.
        if abs groupDelta < Tolerance then
            pathOut.Add(path.GetX(highI), path.GetY(highI), getZ path highI)
        else
            match endType with
            | EndType.Butt -> doBevel path highI highI
            | EndType.Round -> doRound path highI highI Math.PI
            | _ -> doSquare path highI highI

        // Offset the left side going back.
        let mutable k2 = highI
        for i = highI - 1 downto 1 do
            offsetPoint group path i k2
            k2 <- i

        solution.Add(pathOut)


    let doGroupOffset (group: Group<'Z>) : unit =
        if group.EndType = EndType.Polygon then
            // A straight path (2 points) can also be 'polygon' offset where
            // the ends are treated as 180-deg joins.
            if group.LowestPathIdx < 0 then delta <- abs delta
            groupDelta <- if group.PathsReversed then -delta else delta
        else
            groupDelta <- abs delta

        let absDelta = abs groupDelta
        joinType <- group.JoinType
        endType <- group.EndType

        if group.JoinType = JoinType.Round || group.EndType = EndType.Round then
            let arcTol = if arcTolerance > 0.01 then arcTolerance else absDelta * arc_const
            let stepsPer360 = Math.PI / acos (1.0 - arcTol / absDelta)
            stepSin <- sin (2.0 * Math.PI / stepsPer360)
            stepCos <- cos (2.0 * Math.PI / stepsPer360)
            if groupDelta < 0.0 then stepSin <- -stepSin
            stepsPerRad <- stepsPer360 / (2.0 * Math.PI)

        for pathIn in group.InPaths do
            pathOut <- Geo.emptyPath64Sized<'Z> hasZValues (pathIn.PointCount * 2)
            let cnt = pathIn.PointCount

            if cnt = 1 then
                // Single vertex: build a circle or square.
                let ptx = pathIn.GetX(0)
                let pty = pathIn.GetY(0)
                let ptz = getZ pathIn 0

                match deltaCallback with
                | Some cb ->
                    groupDelta <- cb (pathIn, normals, 0, 0)
                    if group.PathsReversed then groupDelta <- -groupDelta
                | None -> ()

                let single =
                    if group.EndType = EndType.Round then
                        let steps = int (ceil (stepsPerRad * 2.0 * Math.PI))
                        ellipse (ptx, pty, abs groupDelta, abs groupDelta, steps, hasZValues, ptz)
                    else
                        let d = ceil (abs groupDelta)
                        let p = Geo.emptyPath64Sized<'Z> hasZValues 4
                        p.Add(ptx - d, pty - d, ptz)
                        p.Add(ptx + d, pty - d, ptz)
                        p.Add(ptx + d, pty + d, ptz)
                        p.Add(ptx - d, pty + d, ptz)
                        p
                solution.Add(single)
            else
                // 2-point joined paths get capped instead.
                if cnt = 2 && group.EndType = EndType.Joined then
                    endType <-
                        if group.JoinType = JoinType.Round then EndType.Round
                        else EndType.Square

                buildNormals pathIn
                match endType with
                | EndType.Polygon -> offsetPolygon group pathIn
                | EndType.Joined -> offsetOpenJoined group pathIn
                // Butt, Square and Round end types for open paths all use the same offsetting logic:
                | _ -> offsetOpenPath group pathIn


    let checkPathsReversed () : bool =
        let mutable result = false
        let mutable found = false
        let mutable i = 0
        while not found && i < groupList.Count do
            if groupList[i].EndType = EndType.Polygon then
                result <- groupList[i].PathsReversed
                found <- true
            i <- i + 1
        result


    let executeInternal (d: float) : unit =
        if groupList.Count = 0 then
            ()
        elif abs d < 0.5 then
            // Make sure the offset delta is significant.
            for group in groupList do
                for path in group.InPaths do
                    solution.Add(path)
        else
            delta <- d
            mitLimSqr <-
                if miterLimit <= 1.0 then 2.0
                else 2.0 / (miterLimit * miterLimit)

            for group in groupList do
                doGroupOffset group

            let pathsReversed = checkPathsReversed ()
            let fillRule = if pathsReversed then FillRule.Negative else FillRule.Positive

            // Clean up self-intersections.
            let c = Clipper64<'Z>()
            c.PreserveCollinear <- preserveCollinear
            c.ReverseSolution <- reverseSolution <> pathsReversed
            c.ZCallback <- zCallback
            c.AddSubject(solution)

            if isNotNull solutionTree then
                c.Execute(ClipType.Union, fillRule, solutionTree) |> ignore
            else
                c.Execute(ClipType.Union, fillRule, solution) |> ignore

    //#endregion
    //#region ClipperOffset members

    /// Tolerance used when generating arc segments.
    /// When 0.0 (default), uses offset_radius / 500.
    member _.ArcTolerance
        with get() : float = arcTolerance
        and set(v: float) : unit = arcTolerance <- v

    /// When true (default) merges all groups before offsetting.
    member _.MergeGroups
        with get() : bool = mergeGroups
        and set(v: bool) : unit = mergeGroups <- v

    /// Limits how far miter joins can extend before being trimmed to bevels.
    member _.MiterLimit
        with get() : float = miterLimit
        and set(v: float) : unit = miterLimit <- v

    /// Whether collinear vertices should be preserved in the output.
    member _.PreserveCollinear
        with get() : bool = preserveCollinear
        and set(v: bool) : unit = preserveCollinear <- v

    /// Reverses the orientation of solution paths.
    member _.ReverseSolution
        with get() : bool = reverseSolution
        and set(v: bool) : unit = reverseSolution <- v

    /// Optional callback invoked when new vertices are produced at edge intersections,
    /// allowing user-defined Z values to be assigned.
    member _.ZCallback
        with get() : ZCallback64<'Z> option = zCallback
        and set(v: ZCallback64<'Z> option) : unit = zCallback <- v

    /// Optional per-vertex delta callback. Receives (path, normals, currIdx, prevIdx)
    /// and returns the delta to apply at the current vertex. The <c>normals</c>
    /// ResizeArray stores X, Y interleaved (normal i is at indices i*2 and i*2+1).
    member _.DeltaCallback
        with get() : DeltaZCallback64<'Z> option = deltaCallback
        and set(v: DeltaZCallback64<'Z> option) : unit = deltaCallback <- v

    /// Removes all input paths.
    member _.Clear() : unit =
        groupList |> Rarr.clear

    /// Adds a single path with the specified join and end types.
    member this.AddPath(path: Path64<'Z>, joinType: JoinType, endType: EndType) : unit =
        if path.PointCount > 0 then
            let pp = Paths64<'Z>()
            pp.Add(path)
            this.AddPaths(pp, joinType, endType)

    /// Adds multiple paths with the specified join and end types.
    member _.AddPaths(paths: Paths64<'Z>, joinType: JoinType, endType: EndType) : unit =
        if paths.Count > 0 then
            for p in paths do
                if p.HasZs then
                    hasZValues <- true
            groupList.Add(Group(paths, joinType, endType))

    /// Performs the offset and writes the result to <c>sol</c>.
    /// Positive <c>delta</c> inflates; negative deflates.
    member _.Execute(delta: float, sol: Paths64<'Z>) : unit =
        sol|> Rarr.clear
        solution <- sol
        solutionTree <- null'()
        executeInternal delta

    /// Performs the offset and writes the result to <c>polyTree</c> as a hierarchy.
    member _.Execute(delta: float, polyTree: PolyTree64<'Z>) : unit =
        polyTree.ClearContent()
        solutionTree <- polyTree
        solution <- Paths64<'Z>()
        executeInternal delta

    /// Performs an offset where the delta is computed per vertex via the callback.
    member this.ExecuteWithCallback(cb: DeltaZCallback64<'Z>, sol: Paths64<'Z>) : unit =
        deltaCallback <- Some cb
        this.Execute(1.0, sol)

//#endregion
