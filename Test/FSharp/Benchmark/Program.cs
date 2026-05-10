using System;
using System.Linq;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Clipper2Lib.Benchmark
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (HasBenchmarkSelection(args))
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance);
            else
                BenchmarkRunner.Run<Benchmarks>();
        }

        private static bool HasBenchmarkSelection(string[] args)
        {
            return args.Any(arg =>
                string.Equals(arg, "--filter", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--list", StringComparison.OrdinalIgnoreCase));
        }
    }
}
