namespace Klip.Tests.Z

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting
open Klip
open Klip.KlipInternal

[<TestClass>]
type ZCallbackTests () =

    /// Build a Path64<int> with all Z values initialised to `zVal`.
    let pathZ (xys: float[]) (zVal: int) : Path64<int> =
        let ptCount = xys.Length / 2
        let zs = ResizeArray<int>(Array.create ptCount zVal)
        Path64.createFromZ zs (ResizeArray xys)

    let mkPaths (ps: Path64<int> list) : Paths64<int> =
        let r = Paths64<int>()
        for p in ps do r.Add p
        r

    [<TestMethod>]
    member _.SelfIntersectingUnionInvokesCallback () =
        // self-intersecting "star" path; callback should fire at every self-intersection
        let subject =
            mkPaths [
                pathZ [| 100.0; 50.0
                         10.0;  79.0
                         65.0;   2.0
                         65.0;  98.0
                         10.0;  21.0 |] 5
            ]

        let calls = ResizeArray<float * float>()
        let callback : ZCallback64<int> =
            fun (_ae1, _ae2, x, y, _z) ->
                calls.Add((x, y))
                1

        let c = Clipper64<int>()
        c.ZCallback <- Some callback
        c.AddSubject(subject)

        let solution = Paths64<int>()
        let ok = c.Execute(ClipType.Union, FillRule.NonZero, solution)
        Assert.IsTrue(ok)
        Assert.AreEqual(1, solution.Count, "expected one resolved polygon")
        Assert.IsTrue(calls.Count > 0, "callback should fire at self-intersections")

    [<TestMethod>]
    member _.TwoTrianglesUnionFiresCallbackAtIntersections () =
        // two overlapping triangles — six intersection points expected
        let subject = mkPaths [ pathZ [| 10.0;30.0; 80.0;30.0; 45.0;90.0 |] 7 ]
        let clip    = mkPaths [ pathZ [| 10.0;70.0; 80.0;70.0; 45.0;10.0 |] 9 ]

        let calls = ResizeArray<float * float>()
        let callback : ZCallback64<int> =
            fun (ae1, ae2, x, y, _z) ->
                Assert.IsTrue(ae1.IsSome, "subject edge should be provided")
                Assert.IsTrue(ae2.IsSome, "clip edge should be provided")
                calls.Add((x, y))
                42

        let c = Clipper64<int>()
        c.ZCallback <- Some callback
        c.AddSubject(subject)
        c.AddClip(clip)

        let solution = Paths64<int>()
        let ok = c.Execute(ClipType.Union, FillRule.NonZero, solution)
        Assert.IsTrue(ok)
        Assert.IsTrue(calls.Count >= 6,
            sprintf "expected >=6 intersection callbacks for two-triangle union, got %d" calls.Count)
        for (x, y) in calls do
            Assert.IsTrue(x >= 0.0 && x <= 100.0)
            Assert.IsTrue(y >= 0.0 && y <= 100.0)

    [<TestMethod>]
    member _.UnionZ_ModuleHelperWiresCallback () =
        let subject = mkPaths [ pathZ [| 0.0;0.0; 10.0;0.0; 10.0;10.0; 0.0;10.0 |] 5 ]
        let clip    = mkPaths [ pathZ [| 5.0;5.0; 15.0;5.0; 15.0;15.0; 5.0;15.0 |] 5 ]

        let mutable invoked = 0
        let callback : ZCallback64<int> =
            fun (_ae1, _ae2, _x, _y, _z) ->
                invoked <- invoked + 1
                42

        let solution = Klipper.unionZ callback clip subject
        Assert.AreEqual(1, solution.Count)
        Assert.IsTrue(invoked > 0, "Klipper.unionZ should route the callback through")

    [<TestMethod>]
    member _.NoIntersectionsMeansNoCallbackInvocations () =
        // disjoint squares — no edges cross
        let subject = mkPaths [ pathZ [| 0.0;0.0; 10.0;0.0; 10.0;10.0; 0.0;10.0 |] 5 ]
        let clip    = mkPaths [ pathZ [| 100.0;100.0; 110.0;100.0; 110.0;110.0; 100.0;110.0 |] 5 ]

        let mutable invoked = 0
        let callback : ZCallback64<int> =
            fun (_ae1, _ae2, _x, _y, _z) ->
                invoked <- invoked + 1
                1

        let c = Clipper64<int>()
        c.ZCallback <- Some callback
        c.AddSubject(subject)
        c.AddClip(clip)
        let solution = Paths64<int>()
        c.Execute(ClipType.Union, FillRule.NonZero, solution) |> ignore

        Assert.AreEqual(0, invoked, "no edges intersect, callback must not fire")
        Assert.AreEqual(2, solution.Count)

    [<TestMethod>]
    member _.OutputPathPropagatesZValues () =
        // Two overlapping triangles; original vertices carry distinct Z markers
        // (subject = 7, clip = 9) and the callback stamps every intersection
        // vertex with 1. The solution path must have a Z buffer aligned with
        // its XY buffer, and every Z must be one of {1, 7, 9}.
        let subject = mkPaths [ pathZ [| 10.0;30.0; 80.0;30.0; 45.0;90.0 |] 7 ]
        let clip    = mkPaths [ pathZ [| 10.0;70.0; 80.0;70.0; 45.0;10.0 |] 9 ]

        let callback : ZCallback64<int> =
            fun (_ae1, _ae2, _x, _y, _z) -> 1

        let c = Clipper64<int>()
        c.ZCallback <- Some callback
        c.AddSubject(subject)
        c.AddClip(clip)

        let solution = Paths64<int>()
        let ok = c.Execute(ClipType.Union, FillRule.NonZero, solution)
        Assert.IsTrue(ok)
        Assert.AreEqual(1, solution.Count)

        let p = solution.[0]
        Assert.IsTrue(p.HasZs, "output path should carry a Z buffer when inputs do")
        match p.Zs with
        | None -> Assert.Fail("expected Some Zs")
        | Some zs ->
            Assert.AreEqual(p.PointCount, zs.Count,
                "Z buffer length must match vertex count")
            let mutable hasIntersect = false
            let mutable hasOriginal = false
            for z in zs do
                Assert.IsTrue(z = 1 || z = 7 || z = 9,
                    sprintf "unexpected Z=%d in output (expected 1, 7, or 9)" z)
                if z = 1 then hasIntersect <- true
                if z = 7 || z = 9 then hasOriginal <- true
            Assert.IsTrue(hasIntersect, "expected callback-set Z=1 at intersections")
            Assert.IsTrue(hasOriginal, "expected original input Zs preserved")

    [<TestMethod>]
    member _.OutputPathHasNoZBufferWhenInputsHaveNone () =
        // sanity: when no input path provides Zs, the engine should not allocate
        // a Z buffer on the output (matches `Path64.create()` path).
        let path (xys: float[]) : Path64<unit> =
            Path64.createFrom  (ResizeArray xys)
        let subj = Paths64.createEmpty()
        subj.Add(path [| 0.0;0.0; 10.0;0.0; 10.0;10.0; 0.0;10.0 |])
        let clip = Paths64.createEmpty()
        clip.Add(path [| 5.0;5.0; 15.0;5.0; 15.0;15.0; 5.0;15.0 |])

        let solution = Klipper.union clip subj
        Assert.AreEqual(1, solution.Count)
        Assert.IsFalse(solution.[0].HasZs,
            "output path should not have a Z buffer when no input paths do")

    [<TestMethod>]
    member _.InputPathRetainsItsOwnZValues () =
        // sanity: Path64 stores user-provided Zs and they're retrievable
        let p = pathZ [| 0.0;0.0; 10.0;0.0; 10.0;10.0 |] 7
        Assert.IsTrue(p.HasZs)
        match p.Zs with
        | Some zs ->
            Assert.AreEqual(3, zs.Count)
            Assert.AreEqual(7, zs.[0])
            Assert.AreEqual(7, zs.[2])
        | None -> Assert.Fail("expected Zs to be Some")
