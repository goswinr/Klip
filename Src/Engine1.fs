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
open Null

/// An enumeration that defines the type of clipping operation to be performed.
/// With the exception of <c>Difference</c>, these operations are commutative, so swapping
/// the subject and clip inputs does not change the result.
type ClipType =
    /// No clipping operation.
    | NoClip = 0
    /// The intersection of subject and clip regions.
    | Intersection = 1
    /// The union of subject and clip regions.
    | Union = 2
    /// The region of subject that is not in the clip region.
    | Difference = 3
    /// The regions of subject or clip that are not in both.
    | Xor = 4

/// Identifies whether input paths belong to the subject set or the clip set.
type PathType =
    | Subject = 0
    | Clip = 1


/// An enumeration that defines which regions of a complex polygon are considered "filled".
/// <c>EvenOdd</c> and <c>NonZero</c> are the most commonly used
/// fill rules, while <c>Positive</c> and <c>Negative</c> restrict filling by winding direction.
type FillRule =
    /// A point is "inside" if a ray from it crosses an odd number of edges.
    | EvenOdd = 0
    /// A point is "inside" if the winding number is non-zero.
    | NonZero = 1
    /// A point is "inside" if the winding number is greater than 0.
    | Positive = 2
    /// A point is "inside" if the winding number is less than 0.
    | Negative = 3

//#region LipInternal

module LipInternal =

    /// Pre-clipping flags attached to each Vertex; combined as bit flags.
    [<Flags>]
    type VertexFlags =
        | None = 0
        | OpenStart = 1
        | OpenEnd = 2
        | LocalMax = 4
        | LocalMin = 8


    /// Values allowed for Active.joinWith.
    type JoinWith =
        | None = 0
        | Left = 1
        | Right = 2


    /// Position of a horizontal edge inside a scanbeam.
    type HorzPosition =
        | Bottom = 0
        | Middle = 1
        | Top = 2


    // Vertex: a pre-clipping data structure. It is used to separate polygons
    // into ascending and descending 'bounds' (or sides) that start at local
    // minima and ascend to a local maxima, before descending again.
    /// A pre-clipping vertex in a doubly-linked ring; carries flags that classify
    /// it relative to local minima / maxima.
    /// Careful: may be NULL when used as a property in other records.
    type [<NoComparison;NoEquality>] Vertex = {
        x: float
        y: float
        z: obj
        mutable next: Vertex | null
        mutable prev: Vertex | null
        mutable flags: VertexFlags
    }

    /// A local minimum of a path, used to seed the active edge list.
    /// Careful: may be NULL when used as a property in other records.
    type  [<NoComparison;NoEquality>] LocalMinima =
        {
        vertex: Vertex
        pathType: PathType
        isOpen: bool
        }


    // OutPt / OutRec / Active / HorzSegment / HorzJoin / PolyPath64
    // are mutually recursive and must be declared together.


    /// Output vertex in a circular linked list representing a clipping solution path.
    /// Careful: may be NULL when used as a property in other records.
    type [<NoComparison;NoEquality>] OutPt =
        {
        x: float
        y: float
        mutable z: obj
        mutable next: OutPt | null
        mutable prev: OutPt | null
        mutable outrec: OutRec
        mutable horz: HorzSegment | null
        }
        static member create (x: float, y: float, z: obj, outrec: OutRec) : OutPt =
            let result = {
                x = x
                y = y
                z = z
                next = null'()
                prev = null'()
                outrec = outrec
                horz = null'()
            }
            result.next <- result
            result.prev <- result
            result

    /// A horizontal edge segment accumulated during Vatti processing.
    /// Careful: may be NULL when used as a property in other records.
    and [<NoComparison;NoEquality>] HorzSegment =
        {
        mutable leftOp: OutPt
        mutable rightOp: OutPt | null
        mutable leftToRight: bool
        }


    /// A pair of OutPts that participate in a horizontal join.
    /// Careful: may be NULL when used as a property in other records.
    and [<NoComparison;NoEquality>] HorzJoin  =
        {
        op1: OutPt
        op2: OutPt
        }


    /// Output record: the clipping solution for a single polygon contour.
    /// Careful: may be NULL when used as a property in other records.
    and [<NoComparison;NoEquality>] OutRec =
        {
        mutable idx: int
        mutable owner: OutRec | null
        mutable frontEdge: ActiveEdge | null
        mutable backEdge: ActiveEdge | null
        mutable pts: OutPt | null
        mutable polypath: PolyPath64 | null
        mutable boundsLeft: float
        mutable boundsTop: float
        mutable boundsRight: float
        mutable boundsBottom: float
        mutable path: Path64
        mutable isOpen: bool
        mutable splits: ResizeArray<int> | null
        mutable recursiveSplit: OutRec | null
        }

    // Important: UP and DOWN here are premised on Y-axis positive down
    // displays, which is the orientation used in Clipper's development.
    /// Active Edge Table node — one for each edge currently intersecting the scanbeam.
    /// Careful: may be NULL when used as a property in other records.
    and [<NoComparison;NoEquality>] ActiveEdge =
        {
        mutable botX: float
        mutable botY: float
        mutable botZ: obj
        mutable topX: float
        mutable topY: float
        mutable topZ: obj
        mutable curX: float // current (updated at every new scanline) - keep as number but ensure integer precision
        mutable dx: float
        mutable windDx: int // 1 or -1 depending on winding direction
        mutable windCount: int
        mutable windCount2: int // winding count of the opposite polytype
        mutable outrec: OutRec | null
        // AEL: 'active edge list' (Vatti's AET - active edge table)
        //     a linked list of all edges (from left to right) that are present
        //     (or 'active') within the current scanbeam (a horizontal 'beam' that
        //     sweeps from bottom to top over the paths in the clipping operation).
        mutable prevInAEL: ActiveEdge | null
        mutable nextInAEL: ActiveEdge | null
        // SEL: 'sorted edge list' (Vatti's ST - sorted table)
        //     linked list used when sorting edges into their new positions at the
        //     top of scanbeams, but also (re)used to process horizontals.
        mutable prevInSEL: ActiveEdge | null
        mutable nextInSEL: ActiveEdge | null
        mutable jump: ActiveEdge | null
        mutable vertexTop: Vertex | null
        // the bottom of an edge 'bound' (also Vatti)
        mutable localMin: LocalMinima | null
        mutable isLeftBound: bool
        mutable joinWith: JoinWith
        }
        static member create (
            botX:      float,
            botY:      float,
            botZ:      obj,
            curX:      float,
            windDx:    int,
            vertexTop: Vertex,
            topX:      float,
            topY:      float,
            topZ:      obj,
            outrec:    OutRec,
            localMin:  LocalMinima) =
                let dx =
                    // getDx inlined here
                    let dy = topY - botY
                    if dy <> 0.0 then          (topX - botX) / dy
                    elif topX > botX then      Double.NegativeInfinity
                    else                       Double.PositiveInfinity
                {
                botX = botX
                botY = botY
                botZ = botZ
                topX = topX
                topY = topY
                topZ = topZ
                curX = curX
                dx = dx
                windDx = windDx
                windCount = 0
                windCount2 = 0
                outrec = outrec
                prevInAEL = null'()
                nextInAEL = null'()
                prevInSEL = null'()
                nextInSEL = null'()
                jump = null'()
                vertexTop = vertexTop
                localMin = localMin
                isLeftBound = false
                joinWith = JoinWith.None
                }

        /// True if this edge belongs to the subject polygon set; false if it belongs to the clip polygon set or if LocalMinima is null.
        member a.IsSubject : bool =
            isNotNull a.localMin && a.localMin.pathType = PathType.Subject

        /// True if this edge belongs to the clip polygon set; false if it belongs to the subject polygon set or if LocalMinima is null.
        member a.IsClip : bool =
            isNotNull a.localMin && a.localMin.pathType = PathType.Clip


    //#endregion
    //#region PolyPath64

    /// A node in a `PolyTree64`. Each `PolyPath64` represents a single contour.
    /// PolyPathBase and PolyPath64 are merged, because PolyPathD does not exist in this F# port.
    and [<AllowNullLiteral>] PolyPath64 (parent: PolyPath64 ) =
        let children = ResizeArray<PolyPath64>()
        let mutable _parent: PolyPath64  = parent
        let mutable polygon: Path64 = null'()

        new() = PolyPath64(null)

        /// Gets or sets the `Path64` vertices of this contour.
        member _.Polygon
            with get() = polygon
            and set(v) = polygon <- v

        /// Gets the parent node.
        member _.Parent = _parent

        /// The nesting level of this contour.
        member _.Level : int =
            let mutable result = 0
            let mutable pp = _parent
            while isNotNull pp do
                result <- result + 1
                pp <- pp.Parent
            result

        /// Boolean indicating if this contour is a hole.
        member this.IsHole : bool =
            let lvl = this.Level
            lvl <> 0 && (lvl &&& 1) = 0

        /// Number of child nodes.
        member _.Count : int =
            children.Count

        /// Clears all child nodes.
        member _.ClearContent() : unit =
            children|> Rarr.clear

        /// Gets the polygon contour. For the polytree root this is null.
        member _.Poly : Path64 =
            polygon

        /// Adds a child contour to this node.
        member this.AddChild(p: Path64) : PolyPath64 =
            let newChild = PolyPath64(this)
            newChild.Polygon <- p
            children.Add(newChild)
            newChild

        /// Accesses child nodes (holes inside this contour, or outer contours inside this hole).
        member _.Child(index: int) : PolyPath64 =
            if index < 0 || index >= children.Count then
                raise (Exception($"PolyPath64.Child index {index} out of range for children count {children.Count}"))
            children[index]

        /// Calculates the total area of this contour and all its children.
        member _.Area() : float =
            let mutable result =
                if isNull' polygon then
                    0.0
                else
                    Geo.area polygon
            for i = 0 to children.Count - 1 do
                result <- result + children[i].Area()
            result

        member internal _.ToStringInternal(idx: int, level: int) : string =
            let sb = Text.StringBuilder()
            let padding = String.replicate level "  "
            let plural = if children.Count = 1 then "" else "s"
            if (level &&& 1) = 0 then
                sb.Append(sprintf "%s+- hole (%d) contains %d nested polygon%s.\n" padding idx children.Count plural) |> ignore
            else
                sb.Append(sprintf "%s+- polygon (%d) contains %d hole%s.\n" padding idx children.Count plural) |> ignore
            for i = 0 to children.Count - 1 do
                if children[i].Count > 0 then
                    sb.Append(children[i].ToStringInternal(i, level + 1)) |> ignore
            sb.ToString()

        override this.ToString() : string =
            if this.Level > 0 then
                "" // only accept tree root
            else
                let plural = if children.Count = 1 then "" else "s"
                let sb = Text.StringBuilder()
                sb.Append(sprintf "Polytree with %d polygon%s.\n" children.Count plural) |> ignore
                for i = 0 to children.Count - 1 do
                    if children[i].Count > 0 then
                        sb.Append(children[i].ToStringInternal(i, 1)) |> ignore
                sb.Append('\n') |> ignore
                sb.ToString()



    /// A specialized data structure used for returning the results of clipping operations.
    /// Unlike `Paths64`, which is a flat list of contours, `PolyTree64` represents the parent-child
    /// relationship between contours (outer contours and their holes), making it essential when the
    /// structure of the resulting polygons matters.
    [<AllowNullLiteral>]
    type PolyTree64() =
        inherit PolyPath64(null)


    // IntersectNode: a structure representing 2 intersecting edges.
    // Intersections must be sorted so they are processed from the largest
    // Y coordinates to the smallest while keeping edges adjacent.
    /// Intersection node: two edges that cross at a given point.
    type [<NoComparison;NoEquality>] IntersectNode = {
        mutable x: float
        mutable y: float
        mutable z: obj
        edge1: ActiveEdge
        edge2: ActiveEdge
    }


    //#endregion
    //#region ScanlineHeap

    // C# keeps scanlines in a sorted list; here we use a heap to avoid O(n) splices.
    /// Max-heap of scanline Y coordinates. Mirrors the TS ScanlineHeap.
    type ScanlineHeap() =
        let data = ResizeArray<float>()

        let siftUp (idx: int) =
            let data = data // avoid closure capture of 'this' in loop?
            let mutable index = idx
            let value = data[index]
            // Hole-sift: lift the value once, shift parents/children, then place.
            // Avoids temporary array allocation from destructuring swap on every step.
            let mutable loopOn = true
            while loopOn && index > 0 do
                let parent = (index - 1) >>> 1
                if data[parent] >= value then
                    loopOn <- false
                else
                    data[index] <- data[parent]
                    index <- parent
            data[index] <- value

        let siftDown (idx: int) =
            let data = data // avoid closure capture of 'this' in loop?
            let mutable index = idx
            let length = data.Count
            let value = data[index]
            let mutable loopOn = true
            while loopOn do
                let left = (index <<< 1) + 1
                if left >= length then
                    loopOn <- false
                else
                    let right = left + 1
                    // Pick the larger child
                    let child =
                        if right < length && data[right] > data[left] then right
                        else left
                    // If the larger child isn't greater than val, done
                    if data[child] <= value then
                        loopOn <- false
                    else
                        data[index] <- data[child]
                        index <- child
            data[index] <- value

        member _.Push(value: float) : unit =
            data.Add(value)
            siftUp (data.Count - 1)

        /// return NaN if empty
        member _.Pop() : float =
            if data.Count = 0 then
                Double.NaN
            else
                let maxV = data[0]
                let last = data[data.Count - 1]
                data |> Rarr.pop
                if data.Count > 0 then
                    data[0] <- last
                    siftDown 0
                maxV

        member _.ClearData() : unit =
            data|> Rarr.clear
