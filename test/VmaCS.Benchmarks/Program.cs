using BenchmarkDotNet.Running;
using VmaCS.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(VmaBenchmarks).Assembly).Run(args);
