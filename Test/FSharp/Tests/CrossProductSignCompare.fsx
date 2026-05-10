#r "nuget: ExtendedNumerics.BigDecimal, 3003.0.0.346"

open System
open ExtendedNumerics

[<Struct>]
type Point64 =
    val mutable X: float
    val mutable Y: float

    new(x: float, y: float) =
        { X = round x
          Y = round y }

[<Struct>]
type UInt128Struct =
    val mutable lo64: uint64
    val mutable hi64: uint64

module InternalClipper =
    let triSign (x: int64) =
        if x < 0L then -1
        elif x > 0L then 1
        else 0

    let multiplyUInt64 (a: uint64) (b: uint64) =
        let x1 = (a &&& 0xFFFFFFFFUL) * (b &&& 0xFFFFFFFFUL)
        let x2 = (a >>> 32) * (b &&& 0xFFFFFFFFUL) + (x1 >>> 32)
        let x3 = (a &&& 0xFFFFFFFFUL) * (b >>> 32) + (x2 &&& 0xFFFFFFFFUL)
        let mutable result = UInt128Struct()
        result.lo64 <- (x3 &&& 0xFFFFFFFFUL) <<< 32 ||| (x1 &&& 0xFFFFFFFFUL)
        result.hi64 <- (a >>> 32) * (b >>> 32) + (x2 >>> 32) + (x3 >>> 32)
        result

    let crossProductSign (pt1: Point64) (pt2: Point64) (pt3: Point64) =
        let a = int64 pt2.X - int64 pt1.X
        let b = int64 pt3.Y - int64 pt2.Y
        let c = int64 pt2.Y - int64 pt1.Y
        let d = int64 pt3.X - int64 pt2.X
        let ab = multiplyUInt64 (uint64 (Math.Abs(a))) (uint64 (Math.Abs(b)))
        let cd = multiplyUInt64 (uint64 (Math.Abs(c))) (uint64 (Math.Abs(d)))
        let signAB = triSign a * triSign b
        let signCD = triSign c * triSign d

        if signAB = signCD then
            let result =
                if ab.hi64 = cd.hi64 then
                    if ab.lo64 = cd.lo64 then 0
                    elif ab.lo64 > cd.lo64 then 1
                    else -1
                elif ab.hi64 > cd.hi64 then 1
                else -1

            if ab.hi64 = cd.hi64 && ab.lo64 = cd.lo64 then 0
            elif signAB > 0 then result
            else -result
        else
            if signAB > signCD then 1 else -1



let crossProductSignFloat (pt1: Point64) (pt2: Point64) (pt3: Point64) =
    let det =
        (pt2.X - pt1.X) * (pt3.Y - pt2.Y) -
        (pt2.Y - pt1.Y) * (pt3.X - pt2.X)

    let sign =
        if det > 0.5 then
            1
        elif det < -0.5 then
            -1
        else
            0

    sign, det

let logCrossProductSignFloat (pt1: Point64) (pt2: Point64) (pt3: Point64) =
    let a = (pt2.X - pt1.X)
    let b = (pt3.Y - pt2.Y)
    let c = (pt2.Y - pt1.Y)
    let d = (pt3.X - pt2.X)
    eprintfn "              = %g * %g - %g * %g" a b c d
    eprintfn "              = %g - %g" (a * b) (c * d)

let toBigDecimal (value: float) =
    BigDecimal.Parse(value)

let crossProductSignBigDecimal (pt1: Point64) (pt2: Point64) (pt3: Point64) =
    let det =
        (toBigDecimal pt2.X - toBigDecimal pt1.X) * (toBigDecimal pt3.Y - toBigDecimal pt2.Y) -
        (toBigDecimal pt2.Y - toBigDecimal pt1.Y) * (toBigDecimal pt3.X - toBigDecimal pt2.X)

    det.Sign, float det

BigDecimal.Precision <- 100
BigDecimal.AlwaysTruncate <- false

printfn "Comparing CrossProductSign with a direct float determinant sign"
printfn "Point64 constructors round coordinates, so this script assigns X/Y directly"
printfn "to simulate Point64 behaving as a true float64 point type."
printfn "pt3 changes in integer steps around a near-collinear large-coordinate case."
printfn "The exact determinant here is largeBase * step, so the expected sign is sign(step)."
printfn ""
printfn ""
let mutable any = false
let skew = 37.0
for sc = -2 to 15 do
    let x = 4503599627368448.0 / 10.0**(float sc) // 2^52 - 2048, still exactly representable
    let y = x * skew
    //let pt1 = Point64(-x, -y)
    let pt1 = Point64(-1.0, -1.0 * skew)
    let pt2 = Point64(x, y)

    printfn "Fixed points: pt1=(%.1f, %.1f), pt2=(%.1f, %.1f)" pt1.X pt1.Y pt2.X pt2.Y
    printfn "%8s  %19s  %10s  %10s  %10s  %18s" "step" "pt3" "CrossProd" "Float" "BigDec" "det <> detf"

    let mutable mismatchCount = 0

    for step in -4 .. 4 do
        let stepFloat = float step
        //let pt3 = Point64(0,  stepFloat)
        let pt3 = Point64(x*0.5, y*0.5 + stepFloat)
        let cpSign = InternalClipper.crossProductSign pt1 pt2 pt3
        let floatSign, detf = crossProductSignFloat pt1 pt2 pt3
        let bigDecimalSign, det = crossProductSignBigDecimal pt1 pt2 pt3

        if cpSign <> floatSign || cpSign <> bigDecimalSign then
            mismatchCount <- mismatchCount + 1
            any <- true
            eprintfn "%8d  (% .0f,% .0f)  %10d  %10d  %10d  %g  %g" step pt3.X pt3.Y cpSign floatSign bigDecimalSign det detf
            logCrossProductSignFloat pt1 pt2 pt3
        else
            printfn "%8d  (% .0f,% .0f)  %10d  %10d  %10d  %.2e  %.2e" step pt3.X pt3.Y cpSign floatSign bigDecimalSign det detf

    printfn ""
    if mismatchCount > 0 then
        eprintfn "sc %d Mismatches: %d" sc mismatchCount
    else
        printfn "sc %d All signs match." sc

if any then
    eprintfn "MISMATCH"
