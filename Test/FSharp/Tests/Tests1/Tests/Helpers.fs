namespace Klip.Tests

open System.Collections.Generic
open Klip

module Helpers =

    /// Build a Path64<unit> from a flat array of interleaved x,y coordinates.
    let path (xys: float[]) : Path64<unit> =
        Path64.createFrom (ResizeArray xys)

    /// Build a Paths64<unit> from a list of paths.
    let paths (ps: Path64<unit> list) : Paths64<unit> =
        let r = Paths64<unit>()
        for p in ps do
            r.Add p
        r


    let totalAbsArea (ps: Paths64<'Z>) : float =
        let mutable s = 0.0
        for p in ps do s <- s + p.AbsArea
        s

    /// Snaps every coordinate of every output path to the nearest integer.
    /// The engine now works with unrounded floats ,
    /// so solution coordinates may carry sub-unit fractional parts. Rounding here,
    /// just before asserting on an output path, restores the integer-grid values
    /// the tests expect. Returns a new Paths64; Z values are preserved.
    let roundPaths (ps: Paths64<'Z>) : Paths64<'Z> =
        Paths64.mapXY (fun v -> System.Math.Round v) ps
