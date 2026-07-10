using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using ClipperPath64 = Clipper2Lib.Path64;
using ClipperPaths64 = Clipper2Lib.Paths64;
using KlipPath64 = Klip.Path64<object>;
using KlipPaths64 = System.Collections.Generic.List<Klip.Path64<object>>;

namespace Clipper2Lib.Benchmark
{
    /// <summary>
    /// Fixture generators mirroring Test/bench/test-data.ts (same shapes and formulas, modulo
    /// .NET's default MidpointRounding.ToEven vs JS Math.round, which only ever differs by one
    /// grid unit at exact .5 boundaries and does not change the structural size/complexity of a
    /// shape). Each case here uses one set of coordinates shared by both the Clipper2 and Klip
    /// runs, so this benchmark measures the same workloads as the JS vitest bench suite
    /// (Test/bench/clipping-operations.bench.ts) rather than Benchmarks.cs's dense random-polygon
    /// dataset, which barely exercises AEL fast paths because nearly every scanbeam intersects.
    /// </summary>
    internal static class VitestFixtures
    {
        private static long R(double v) => (long)Math.Round(v);

        public static ClipperPath64 GenerateCircle(double radius, double centerX, double centerY, int numPoints)
        {
            ClipperPath64 path = new(numPoints);
            for (int i = 0; i < numPoints; i++)
            {
                double angle = 2 * Math.PI * i / numPoints;
                path.Add(new Point64(R(centerX + radius * Math.Cos(angle)), R(centerY + radius * Math.Sin(angle))));
            }
            return path;
        }

        public static ClipperPath64 GenerateRectangle(double left, double top, double right, double bottom)
        {
            ClipperPath64 path = new(4);
            path.Add(new Point64(R(left), R(top)));
            path.Add(new Point64(R(right), R(top)));
            path.Add(new Point64(R(right), R(bottom)));
            path.Add(new Point64(R(left), R(bottom)));
            return path;
        }

        public static ClipperPath64 GenerateComplexPolygon(int numVertices)
        {
            const double centerX = 1000, centerY = 1000, baseRadius = 500;
            ClipperPath64 path = new(numVertices);
            for (int i = 0; i < numVertices; i++)
            {
                double angle = 2 * Math.PI * i / numVertices;
                double radiusVariation = Math.Sin(angle * 5) * 100;
                double radius = baseRadius + radiusVariation;
                path.Add(new Point64(R(centerX + radius * Math.Cos(angle)), R(centerY + radius * Math.Sin(angle))));
            }
            return path;
        }

        public static ClipperPaths64 GenerateGrid(int rows, int cols, double cellSize, double gap)
        {
            ClipperPaths64 paths = new();
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    double x = col * (cellSize + gap);
                    double y = row * (cellSize + gap);
                    paths.Add(GenerateRectangle(x, y, x + cellSize, y + cellSize));
                }
            return paths;
        }

        public static ClipperPath64 TranslatePath(ClipperPath64 path, double dx, double dy)
        {
            long ddx = R(dx), ddy = R(dy);
            ClipperPath64 result = new(path.Count);
            foreach (Point64 p in path)
                result.Add(new Point64(p.X + ddx, p.Y + ddy));
            return result;
        }

        private static ClipperPaths64 Single(ClipperPath64 path) => new() { path };

        // ---- testData (Test/bench/test-data.ts) ----
        public static readonly ClipperPath64 MediumComplex = GenerateComplexPolygon(100);
        public static readonly ClipperPath64 LargeComplex = GenerateComplexPolygon(500);
        public static readonly ClipperPath64 VeryLargeComplex = GenerateComplexPolygon(2000);
        public static readonly ClipperPath64 MediumRect = GenerateRectangle(0, 0, 500, 500);
        public static readonly ClipperPaths64 MediumGrid = GenerateGrid(5, 5, 100, 20);
        public static readonly ClipperPaths64 LargeGrid = GenerateGrid(10, 10, 50, 10);

        // ---- overlappingPairs ----
        public static readonly ClipperPaths64 MediumSubject = Single(MediumComplex);
        public static readonly ClipperPaths64 MediumClip = Single(TranslatePath(MediumComplex, 200, 200));
        public static readonly ClipperPaths64 LargeSubject = Single(LargeComplex);
        public static readonly ClipperPaths64 LargeClip = Single(TranslatePath(LargeComplex, 400, 400));
        public static readonly ClipperPaths64 GridSubject = MediumGrid;
        public static readonly ClipperPaths64 GridClip = Single(TranslatePath(MediumRect, 150, 150));

        // ---- Simple Union Operations ----
        public static readonly ClipperPaths64 SimpleRects = new()
        {
            GenerateRectangle(0, 0, 100, 100),
            GenerateRectangle(150, 150, 250, 250),
            GenerateRectangle(300, 0, 400, 100),
        };

        public static readonly ClipperPaths64 TwoOverlapping = new()
        {
            GenerateRectangle(0, 0, 100, 100),
            GenerateRectangle(50, 50, 150, 150),
        };

        public static readonly ClipperPaths64 FourCircles = BuildFourCircles();

        private static ClipperPaths64 BuildFourCircles()
        {
            ClipperPath64 simplePath = new(8);
            for (int i = 0; i < 8; i++)
            {
                double angle = i / 8.0 * Math.PI * 2;
                simplePath.Add(new Point64(R(Math.Cos(angle) * 50 + 100), R(Math.Sin(angle) * 50 + 100)));
            }
            return new ClipperPaths64
            {
                simplePath,
                TranslatePath(simplePath, 200, 0),
                TranslatePath(simplePath, 0, 200),
                TranslatePath(simplePath, 200, 200),
            };
        }

        // ---- Geo-scale Coordinates ----
        private const double GeoScale = 360_000;

        public static readonly ClipperPath64 GeoComplex = BuildGeoComplex();
        public static readonly ClipperPath64 GeoComplexShifted = TranslatePath(GeoComplex, R(200 * GeoScale), R(200 * GeoScale));

        private static ClipperPath64 BuildGeoComplex()
        {
            ClipperPath64 result = new(MediumComplex.Count);
            foreach (Point64 p in MediumComplex)
                result.Add(new Point64(R(p.X * GeoScale), R(p.Y * GeoScale)));
            return result;
        }

        // ---- Klip conversions (same helpers as Benchmarks.cs) ----
        public static KlipPaths64 ToKlipPaths(ClipperPaths64 paths)
        {
            KlipPaths64 result = new(paths.Count);
            foreach (ClipperPath64 path in paths)
                result.Add(ToKlipPath(path));
            return result;
        }

        public static KlipPath64 ToKlipPath(ClipperPath64 path)
        {
            List<double> coords = new(path.Count * 2);
            foreach (Point64 point in path)
            {
                coords.Add(point.X);
                coords.Add(point.Y);
            }
            return new KlipPath64(coords, null);
        }
    }

    // Unlike Benchmarks.cs's dense random-polygon dataset (individual ops taking
    // 100us-38ms, well suited to FastConfig's fixed InvocationCount=128), most fixtures
    // here are cheap enough that FastConfig's fixed invocation count produced sub-100ms
    // iterations and highly noisy results (BenchmarkDotNet's own "MinIterationTime" warning).
    // Use the default adaptive job instead so BenchmarkDotNet picks a large enough
    // invocation count per benchmark for a stable measurement.
    [MemoryDiagnoser]
    public class VitestFixtureBenchmarks
    {
        private sealed class Case
        {
            public readonly ClipperPaths64 Subject;
            public readonly ClipperPaths64 Clip;
            public readonly KlipPaths64 KlipSubject;
            public readonly KlipPaths64 KlipClip;

            public Case(ClipperPaths64 subject, ClipperPaths64 clip = null)
            {
                Subject = subject;
                Clip = clip;
                KlipSubject = VitestFixtures.ToKlipPaths(subject);
                KlipClip = clip is null ? null : VitestFixtures.ToKlipPaths(clip);
            }
        }

        private static readonly Case IntersectionMedium = new(VitestFixtures.MediumSubject, VitestFixtures.MediumClip);
        private static readonly Case IntersectionLarge = new(VitestFixtures.LargeSubject, VitestFixtures.LargeClip);
        private static readonly Case IntersectionGrid = new(VitestFixtures.GridSubject, VitestFixtures.GridClip);
        private static readonly Case DifferenceMedium = new(VitestFixtures.MediumSubject, VitestFixtures.MediumClip);
        private static readonly Case DifferenceLarge = new(VitestFixtures.LargeSubject, VitestFixtures.LargeClip);
        private static readonly Case XorMedium = new(VitestFixtures.MediumSubject, VitestFixtures.MediumClip);
        private static readonly Case XorLarge = new(VitestFixtures.LargeSubject, VitestFixtures.LargeClip);

        private static readonly Case UnionMediumComplex = new(new ClipperPaths64 { VitestFixtures.MediumComplex });
        private static readonly Case UnionLargeComplex = new(new ClipperPaths64 { VitestFixtures.LargeComplex });
        private static readonly Case UnionVeryLargeComplex = new(new ClipperPaths64 { VitestFixtures.VeryLargeComplex });
        private static readonly Case UnionMediumGrid = new(VitestFixtures.MediumGrid);
        private static readonly Case UnionLargeGrid = new(VitestFixtures.LargeGrid);
        private static readonly Case UnionSimpleRects = new(VitestFixtures.SimpleRects);
        private static readonly Case UnionTwoOverlapping = new(VitestFixtures.TwoOverlapping);
        private static readonly Case UnionFourCircles = new(VitestFixtures.FourCircles);

        private static readonly Case GeoUnion = new(new ClipperPaths64 { VitestFixtures.GeoComplex });
        private static readonly Case GeoIntersection = new(
            new ClipperPaths64 { VitestFixtures.GeoComplex },
            new ClipperPaths64 { VitestFixtures.GeoComplexShifted });

        private readonly ClipperPaths64 _clipperSolution = new();

        private void RunClipper(ClipType ct, Case c)
        {
            Clipper64 clipper = new();
            clipper.AddSubject(c.Subject);
            if (c.Clip is not null) clipper.AddClip(c.Clip);
            clipper.Execute(ct, FillRule.NonZero, _clipperSolution);
        }

        private static void RunKlip(Klip.ClipType ct, Case c)
        {
            Klip.Clipper64<object> clipper = new();
            clipper.AddSubject(c.KlipSubject);
            if (c.KlipClip is not null) clipper.AddClip(c.KlipClip);
            clipper.Execute(ct, Klip.FillRule.NonZero);
        }

        [Benchmark] public void Clipper2_Intersection_Medium() => RunClipper(ClipType.Intersection, IntersectionMedium);
        [Benchmark] public void Klip_Intersection_Medium() => RunKlip(Klip.ClipType.Intersection, IntersectionMedium);

        [Benchmark] public void Clipper2_Intersection_Large() => RunClipper(ClipType.Intersection, IntersectionLarge);
        [Benchmark] public void Klip_Intersection_Large() => RunKlip(Klip.ClipType.Intersection, IntersectionLarge);

        [Benchmark] public void Clipper2_Intersection_Grid() => RunClipper(ClipType.Intersection, IntersectionGrid);
        [Benchmark] public void Klip_Intersection_Grid() => RunKlip(Klip.ClipType.Intersection, IntersectionGrid);

        [Benchmark] public void Clipper2_Difference_Medium() => RunClipper(ClipType.Difference, DifferenceMedium);
        [Benchmark] public void Klip_Difference_Medium() => RunKlip(Klip.ClipType.Difference, DifferenceMedium);

        [Benchmark] public void Clipper2_Difference_Large() => RunClipper(ClipType.Difference, DifferenceLarge);
        [Benchmark] public void Klip_Difference_Large() => RunKlip(Klip.ClipType.Difference, DifferenceLarge);

        [Benchmark] public void Clipper2_Xor_Medium() => RunClipper(ClipType.Xor, XorMedium);
        [Benchmark] public void Klip_Xor_Medium() => RunKlip(Klip.ClipType.Xor, XorMedium);

        [Benchmark] public void Clipper2_Xor_Large() => RunClipper(ClipType.Xor, XorLarge);
        [Benchmark] public void Klip_Xor_Large() => RunKlip(Klip.ClipType.Xor, XorLarge);

        [Benchmark] public void Clipper2_Union_MediumComplex() => RunClipper(ClipType.Union, UnionMediumComplex);
        [Benchmark] public void Klip_Union_MediumComplex() => RunKlip(Klip.ClipType.Union, UnionMediumComplex);

        [Benchmark] public void Clipper2_Union_LargeComplex() => RunClipper(ClipType.Union, UnionLargeComplex);
        [Benchmark] public void Klip_Union_LargeComplex() => RunKlip(Klip.ClipType.Union, UnionLargeComplex);

        [Benchmark] public void Clipper2_Union_VeryLargeComplex() => RunClipper(ClipType.Union, UnionVeryLargeComplex);
        [Benchmark] public void Klip_Union_VeryLargeComplex() => RunKlip(Klip.ClipType.Union, UnionVeryLargeComplex);

        [Benchmark] public void Clipper2_Union_MediumGrid() => RunClipper(ClipType.Union, UnionMediumGrid);
        [Benchmark] public void Klip_Union_MediumGrid() => RunKlip(Klip.ClipType.Union, UnionMediumGrid);

        [Benchmark] public void Clipper2_Union_LargeGrid() => RunClipper(ClipType.Union, UnionLargeGrid);
        [Benchmark] public void Klip_Union_LargeGrid() => RunKlip(Klip.ClipType.Union, UnionLargeGrid);

        [Benchmark] public void Clipper2_Union_SimpleRects() => RunClipper(ClipType.Union, UnionSimpleRects);
        [Benchmark] public void Klip_Union_SimpleRects() => RunKlip(Klip.ClipType.Union, UnionSimpleRects);

        [Benchmark] public void Clipper2_Union_TwoOverlapping() => RunClipper(ClipType.Union, UnionTwoOverlapping);
        [Benchmark] public void Klip_Union_TwoOverlapping() => RunKlip(Klip.ClipType.Union, UnionTwoOverlapping);

        [Benchmark] public void Clipper2_Union_FourCircles() => RunClipper(ClipType.Union, UnionFourCircles);
        [Benchmark] public void Klip_Union_FourCircles() => RunKlip(Klip.ClipType.Union, UnionFourCircles);

        [Benchmark] public void Clipper2_Geo_Union() => RunClipper(ClipType.Union, GeoUnion);
        [Benchmark] public void Klip_Geo_Union() => RunKlip(Klip.ClipType.Union, GeoUnion);

        [Benchmark] public void Clipper2_Geo_Intersection() => RunClipper(ClipType.Intersection, GeoIntersection);
        [Benchmark] public void Klip_Geo_Intersection() => RunKlip(Klip.ClipType.Intersection, GeoIntersection);
    }
}
