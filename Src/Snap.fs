
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
    type private Coordinate =
        { value: float; buffer: ResizeArray<float>; index: int }

    /// Suggested per-axis distance, matching Clipper64's default distance tolerance.
    /// Pass it explicitly: the snapping functions always require a tolerance.
    [<Literal>]
    let DefaultTolerance : float = 1e-5

    let private snapAxis tolerance (coordinates: ResizeArray<Coordinate>) =
        coordinates.Sort(fun a b -> compare a.value b.value)
        let mutable start = 0
        while start < coordinates.Count do
            let origin = coordinates[start].value
            let mutable finish = start + 1
            let mutable meanDelta = 0.
            // Bound the entire cluster width. Near-equality is not transitive.
            while finish < coordinates.Count && coordinates[finish].value - origin <= tolerance do
                let delta = coordinates[finish].value - origin
                meanDelta <- meanDelta + (delta - meanDelta) / float (finish - start + 1)
                finish <- finish + 1
            // Averaging local deltas avoids overflowing a sum of absolute coordinates,
            // and leaves identical coordinates exactly unchanged, including at zero tolerance.
            let mean = origin + meanDelta
            for i = start to finish - 1 do
                let coordinate = coordinates[i]
                coordinate.buffer[coordinate.index] <- mean
            start <- finish

    /// Snaps closed paths across all collections together, in place. A vertex qualifies
    /// on an axis when either of its actual neighbours is within tolerance on that axis.
    /// Each vertex contributes once. Sorted clusters have width at most tolerance and
    /// snap to their mean; a chain of near neighbours does not form one unbounded cluster.
    /// This is a single preprocessing pass, not an iterative convergence operation.
    /// All coordinates and the nonnegative tolerance must be finite; validation is atomic.
    let xAndY (tolerance: float) (pathCollections: seq<Paths64<'Z>>) : unit =
        if not (tolerance >= 0.) || Double.IsInfinity tolerance then
            invalidArg "tolerance" "Snap tolerance must be finite and nonnegative."
        let xs = ResizeArray<Coordinate>()
        let ys = ResizeArray<Coordinate>()
        // Collect before writing anything, so qualification uses the original geometry
        // and a bad coordinate in a later collection cannot leave partially snapped input.
        for paths in pathCollections do
            for path in paths do
                let xys = path.XYs
                for value in xys do
                    if Double.IsNaN value || Double.IsInfinity value then
                        invalidArg "pathCollections" "Snap coordinates must be finite."
                let count = path.PointCount
                for i = 0 to count - 1 do
                    let previous = (i + count - 1) % count
                    let next = (i + 1) % count
                    for axis = 0 to 1 do
                        let index = 2*i + axis
                        let value = xys[index]
                        if abs (value - xys[2*previous+axis]) <= tolerance ||
                           abs (value - xys[2*next+axis]) <= tolerance then
                            let target = if axis = 0 then xs else ys
                            target.Add { value = value; buffer = xys; index = index }
        snapAxis tolerance xs
        snapAxis tolerance ys

    /// <summary>
    /// Snaps the X and Y coordinates of every path in <paramref name="paths"/> in place.
    /// Convenience wrapper over <see cref="M:Klip.Snap.xAndY"/> for a single collection.
    /// </summary>
    /// <param name="tolerance">Per-axis clustering distance (absolute coordinate units).</param>
    /// <param name="paths">The paths whose coordinate buffers are mutated in place.</param>
    let xAndYSingle (tolerance: float) (paths: Paths64<'Z>) : unit =
        xAndY tolerance [| paths |]