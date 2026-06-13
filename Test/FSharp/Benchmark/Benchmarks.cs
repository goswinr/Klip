using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using ClipperPath64 = Clipper2Lib.Path64;
using ClipperPaths64 = Clipper2Lib.Paths64;
using KlipPath64 = Klip.Path64<object>;
using KlipPaths64 = System.Collections.Generic.List<Klip.Path64<object>>;

namespace Clipper2Lib.Benchmark
{
    public class FastConfig : ManualConfig
    {
        public FastConfig()
        {
            Add(DefaultConfig.Instance);
            AddJob(Job.Default
                .WithId("Quick")
                .WithStrategy(RunStrategy.Throughput)
                .WithLaunchCount(1)
                .WithWarmupCount(2)
                .WithIterationCount(2)
                .WithInvocationCount(128)
                .WithUnrollFactor(2)
            );
        }
    }

    [MemoryDiagnoser]
    [Config(typeof(FastConfig))] // comment out for marginally more accurate results
    public class Benchmarks
    {
        private ClipperPaths64 _clipperSubj;
        private ClipperPaths64 _clipperClip;
        private ClipperPaths64 _clipperSolution;
        private KlipPaths64 _klipSubj;
        private KlipPaths64 _klipClip;
        private KlipPaths64 _klipSolution;
        private const int DisplayWidth = 800;
        private const int DisplayHeight = 600;
        private const int RandomSeed = 12345;

        [Params( 100 , 500/*, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000*/)]
        public int EdgeCount { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            Random rand = new(RandomSeed);

            _clipperSubj = new ClipperPaths64();
            _clipperClip = new ClipperPaths64();
            _clipperSolution = new ClipperPaths64();

            _clipperSubj.Add(MakeRandomPath(DisplayWidth, DisplayHeight, EdgeCount, rand));
            _clipperClip.Add(MakeRandomPath(DisplayWidth, DisplayHeight, EdgeCount, rand));

            _klipSubj = ToKlipPaths(_clipperSubj);
            _klipClip = ToKlipPaths(_clipperClip);
            _klipSolution = new KlipPaths64();
        }

        [Benchmark]
        public void Clipper2_Intersection()
        {
            Clipper64 c = new();
            c.AddSubject(_clipperSubj);
            c.AddClip(_clipperClip);
            c.Execute(ClipType.Intersection, FillRule.NonZero, _clipperSolution);
        }

        [Benchmark]
        public void Klip_Intersection()
        {
            Klip.Clipper64<object> c = new();
            c.AddSubject(_klipSubj);
            c.AddClip(_klipClip);
            c.Execute(Klip.ClipType.Intersection, Klip.FillRule.NonZero);
        }

        [Benchmark]
        public void Clipper2_Union()
        {
            Clipper64 c = new();
            c.AddSubject(_clipperSubj);
            c.AddClip(_clipperClip);
            c.Execute(ClipType.Union, FillRule.NonZero, _clipperSolution);
        }

        [Benchmark]
        public void Klip_Union()
        {
            Klip.Clipper64<object> c = new();
            c.AddSubject(_klipSubj);
            c.AddClip(_klipClip);
            c.Execute(Klip.ClipType.Union, Klip.FillRule.NonZero);
        }

        [Benchmark]
        public void Clipper2_Difference()
        {
            Clipper64 c = new();
            c.AddSubject(_clipperSubj);
            c.AddClip(_clipperClip);
            c.Execute(ClipType.Difference, FillRule.NonZero, _clipperSolution);
        }

        [Benchmark]
        public void Klip_Difference()
        {
            Klip.Clipper64<object> c = new();
            c.AddSubject(_klipSubj);
            c.AddClip(_klipClip);
            c.Execute(Klip.ClipType.Difference, Klip.FillRule.NonZero);
        }

        [Benchmark]
        public void Clipper2_Xor()
        {
            Clipper64 c = new();
            c.AddSubject(_clipperSubj);
            c.AddClip(_clipperClip);
            c.Execute(ClipType.Xor, FillRule.NonZero, _clipperSolution);
        }

        [Benchmark]
        public void Klip_Xor()
        {
            Klip.Clipper64<object> c = new();
            c.AddSubject(_klipSubj);
            c.AddClip(_klipClip);
            c.Execute(Klip.ClipType.Xor, Klip.FillRule.NonZero);
        }

        private static Point64 MakeRandomPt(int maxWidth, int maxHeight, Random rand)
        {
            long x = rand.Next(maxWidth);
            long y = rand.Next(maxHeight);
            return new Point64(x, y);
        }

        public static ClipperPath64 MakeRandomPath(int width, int height, int count, Random rand)
        {
            ClipperPath64 result = new(count);
            for (int i = 0; i < count; ++i)
                result.Add(MakeRandomPt(width, height, rand));
            return result;
        }

        private static KlipPaths64 ToKlipPaths(ClipperPaths64 paths)
        {
            KlipPaths64 result = new(paths.Count);
            foreach (ClipperPath64 path in paths)
                result.Add(ToKlipPath(path));
            return result;
        }

        private static KlipPath64 ToKlipPath(ClipperPath64 path)
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
}
