namespace Klip.Tests

open System.Collections.Generic
open Klip

module Helpers =

    /// Build a Path64<unit> from a flat array of interleaved x,y coordinates.
    let path (xys: float[]) : Path64<unit> =
        Path64.createFrom 1.0 (ResizeArray xys)

    /// Build a Paths64<unit> from a list of paths.
    let paths (ps: Path64<unit> list) : Paths64<unit> =
        let r = Paths64<unit>()
        for p in ps do r.Add p
        r

    /// Shoelace area (positive for CCW in Cartesian / CW in screen coords).
    let signedArea (p: Path64<'Z>) : float =
        let cnt = p.PointCount
        if cnt < 3 then 0.0
        else
            let xys = p.XYs
            let mutable total = 0.0
            let mutable prev = (cnt - 1) * 2
            let mutable px = xys.[prev]
            let mutable py = xys.[prev + 1]
            for i = 0 to cnt - 1 do
                let c = i * 2
                let x = xys.[c]
                let y = xys.[c + 1]
                total <- total + (py + y) * (px - x)
                px <- x
                py <- y
            total * 0.5

    let absArea (p: Path64<'Z>) : float =
        abs (signedArea p)

    let totalAbsArea (ps: Paths64<'Z>) : float =
        let mutable s = 0.0
        for p in ps do s <- s + absArea p
        s
