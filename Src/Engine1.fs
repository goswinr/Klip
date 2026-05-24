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


//#region Klip public types

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
/// <c>EvenOdd</c> and <c>NonZero</c> are the most commonly used fill rules,
/// while <c>Positive</c> and <c>Negative</c> restrict filling by winding direction.
type FillRule =
    /// A point is "inside" if a ray from it crosses an odd number of edges.
    | EvenOdd = 0
    /// A point is "inside" if the winding number is non-zero.
    | NonZero = 1
    /// A point is "inside" if the winding number is greater than 0.
    | Positive = 2
    /// A point is "inside" if the winding number is less than 0.
    | Negative = 3

//#endregion
//#region KlipInternal types

module KlipInternal =

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
    type [<NoComparison;NoEquality>] Vertex<'Z> = {
        x: float
        y: float
        z: 'Z
        mutable next: Vertex<'Z> | null
        mutable prev: Vertex<'Z> | null
        mutable flags: VertexFlags
    }

    /// A local minimum of a path, used to seed the active edge list.
    type [<Struct; NoComparison;NoEquality>] LocalMinima<'Z> = // a struct in C# passed by reference
        {
        vertex: Vertex<'Z>
        pathType: PathType
        isOpen: bool
        }


    // OutPt / OutRec / Active / HorzSegment / HorzJoin / PolyPath64
    // are mutually recursive and must be declared together.


    /// Output vertex in a circular linked list representing a clipping solution path.
    /// Careful: may be NULL when used as a property in other records.
    type [<NoComparison;NoEquality>] OutPt<'Z> =
        {
        x: float
        y: float
        mutable z: 'Z
        mutable next: OutPt<'Z> | null
        mutable prev: OutPt<'Z> | null
        mutable outrec: OutRec<'Z>
        mutable horz: HorzSegment<'Z> | null
        }
        static member inline create (x: float, y: float, z: 'Z, outrec: OutRec<'Z>) : OutPt<'Z> =
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
    and [<NoComparison;NoEquality>] HorzSegment<'Z> =
        {
        mutable leftOp: OutPt<'Z> | null
        mutable rightOp: OutPt<'Z> | null
        mutable leftToRight: bool
        }


    /// A pair of OutPts that participate in a horizontal join.
    /// Careful: may be NULL when used as a property in other records.
    and [<NoComparison;NoEquality>] HorzJoin<'Z>  =
        {
        op1: OutPt<'Z>
        op2: OutPt<'Z>
        }


    /// Output record: the clipping solution for a single polygon contour.
    /// Careful: may be NULL when used as a property in other records.
    and [<NoComparison;NoEquality>] OutRec<'Z> =
        {
        mutable idx: int
        mutable owner: OutRec<'Z> | null
        mutable frontEdge: ActiveEdge<'Z> | null
        mutable backEdge: ActiveEdge<'Z> | null
        mutable pts: OutPt<'Z> | null
        mutable polypath: PolyPath64<'Z> | null
        mutable boundsLeft: float
        mutable boundsTop: float
        mutable boundsRight: float
        mutable boundsBottom: float
        mutable path: Path64<'Z>
        mutable isOpen: bool
        mutable splits: ResizeArray<int> | null
        mutable recursiveSplit: OutRec<'Z> | null
        }

    /// Active Edge Table node — one for each edge currently intersecting the scanbeam.
    /// Important: UP and DOWN here are premised on Y-axis positive down
    /// displays, which is the orientation used in Clipper's development.
    and [<NoComparison;NoEquality>] ActiveEdge<'Z> =
        {
        mutable botX: float
        mutable botY: float
        mutable botZ: 'Z
        mutable topX: float
        mutable topY: float
        mutable topZ: 'Z
        mutable curX: float // current (updated at every new scanline) - keep as number but ensure integer precision
        mutable dx: float
        mutable windDx: int // 1 or -1 depending on winding direction
        mutable windCount: int
        mutable windCount2: int // winding count of the opposite polytype
        mutable outrec: OutRec<'Z> | null
        // AEL: 'active edge list' (Vatti's AET - active edge table)
        //     a linked list of all edges (from left to right) that are present
        //     (or 'active') within the current scanbeam (a horizontal 'beam' that
        //     sweeps from bottom to top over the paths in the clipping operation).
        mutable prevInAEL: ActiveEdge<'Z> | null
        mutable nextInAEL: ActiveEdge<'Z> | null
        // SEL: 'sorted edge list' (Vatti's ST - sorted table)
        //     linked list used when sorting edges into their new positions at the
        //     top of scanbeams, but also (re)used to process horizontals.
        mutable prevInSEL: ActiveEdge<'Z> | null
        mutable nextInSEL: ActiveEdge<'Z> | null
        mutable jump: ActiveEdge<'Z> | null
        mutable vertexTop: Vertex<'Z> | null
        // the bottom of an edge 'bound' (also Vatti)
        mutable isLeftBound: bool
        mutable joinWith: JoinWith
        localMin: LocalMinima<'Z>
        }
        static member create (
            botX:      float,
            botY:      float,
            botZ:      'Z,
            curX:      float,
            windDx:    int,
            vertexTop: Vertex<'Z>,
            topX:      float,
            topY:      float,
            topZ:      'Z,
            outrec:    OutRec<'Z>,
            localMin:  LocalMinima<'Z>) =
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
                isLeftBound = false
                joinWith = JoinWith.None
                localMin = localMin
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
    and [<AllowNullLiteral>] PolyPath64<'Z> (parent: PolyPath64<'Z> ) =
        // PolyPathBase and PolyPath64 are merged, because PolyPathD does not exist in this F# port.

        let children = ResizeArray<PolyPath64<'Z>>()

        let mutable _parent: PolyPath64<'Z>  = parent

        let mutable polygon: Path64<'Z> = null'()

        new() =
            PolyPath64(null)

        /// Gets or sets the `Path64` vertices of this contour.
        member _.Polygon
            with get() = polygon
            and set(v) = polygon <- v

        /// Gets the parent node.
        member _.Parent =
            _parent

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
            children |> Rarr.clear

        /// Gets the polygon contour. For the polytree root this is null.
        member _.Poly : Path64<'Z> =
            polygon

        /// Adds a child contour to this node.
        member this.AddChild(p: Path64<'Z>) : PolyPath64<'Z> =
            let newChild = PolyPath64<'Z>(this)
            newChild.Polygon <- p
            children.Add(newChild)
            newChild

        /// Accesses child nodes (holes inside this contour, or outer contours inside this hole).
        member _.Child(index: int) : PolyPath64<'Z> =
            if index < 0 || index >= children.Count then
                raise (Exception($"PolyPath64.Child index {index} out of range for children count {children.Count}"))
            children[index]

        /// Calculates the total area of this contour and all its children.
        member _.Area() : float =
            let mutable result =
                if isNull' polygon then
                    0.0
                else
                    polygon.SignedArea
            for i = 0 to children |> Rarr.lastIdx do
                result <- result + children[i].Area()
            result

        member internal _.ToStringInternal(idx: int, level: int) : string =
            let sb = Text.StringBuilder()
            let padding = String.replicate level "  "
            let plural = if children.Count = 1 then "" else "s"
            if level &&& 1 = 0 then
                sb.AppendLine $"{padding}+- hole ({idx}) contains {children.Count} nested polygon{plural}." |> ignore
            else
                sb.AppendLine $"{padding}+- polygon ({idx}) contains {children.Count} hole{plural}." |> ignore
            for i = 0 to children |> Rarr.lastIdx do
                if children[i].Count > 0 then
                    sb.Append(children[i].ToStringInternal(i, level + 1)) |> ignore
            sb.ToString()

        override this.ToString() : string =
            if this.Level > 0 then
                "" // only accept tree root
            else
                let plural = if children.Count = 1 then "" else "s"
                let sb = Text.StringBuilder()
                sb.AppendLine $"Polytree with {children.Count} polygon{plural}." |> ignore
                for i = 0 to children |> Rarr.lastIdx do
                    if children[i].Count > 0 then
                        sb.Append(children[i].ToStringInternal(i, 1)) |> ignore
                sb.Append('\n') |> ignore
                sb.ToString()



    /// A specialized data structure used for returning the results of clipping operations.
    /// Unlike `Paths64`, which is a flat list of contours, `PolyTree64` represents the parent-child
    /// relationship between contours (outer contours and their holes), making it essential when the
    /// structure of the resulting polygons matters.
    [<AllowNullLiteral>]
    type PolyTree64<'Z>() =
        inherit PolyPath64<'Z>(null)


    // IntersectNode: a structure representing 2 intersecting edges.
    // Intersections must be sorted so they are processed from the largest
    // Y coordinates to the smallest while keeping edges adjacent.
    /// Intersection node: two edges that cross at a given point.
    // [<Struct>]
    [<NoComparison;NoEquality>]
    type IntersectNode<'Z> = { // a struct in C# passed by reference
        mutable x: float
        mutable y: float
        mutable z: 'Z
        edge1: ActiveEdge<'Z>
        edge2: ActiveEdge<'Z>
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
            let length = Rarr.len data
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
            siftUp (Rarr.lastIdx data)

        /// return NaN if empty
        member _.Pop() : float =
            if Rarr.len data = 0 then
                Double.NaN
            else
                let maxV = data[0]
                let last = data[Rarr.lastIdx data]
                data |> Rarr.pop
                if Rarr.len data > 0 then
                    data[0] <- last
                    siftDown 0
                maxV

        member _.ClearData() : unit =
            data|> Rarr.clear
