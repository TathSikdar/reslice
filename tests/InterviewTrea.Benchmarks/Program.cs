using BenchmarkDotNet.Running;

// Release only, and excluded from CI: a benchmark run takes minutes and its numbers are
// meaningless on shared build hardware. Results are captured by hand into
// docs/performance.md.
BenchmarkSwitcher.FromAssembly(typeof(InterviewTrea.Benchmarks.ResliceBenchmarks).Assembly)
    .Run(args);
