using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using ErrorOr.Interceptors.Benchmarks;

// Run with: dotnet run -c Release
BenchmarkRunner.Run<InterceptorBenchmarks>(
    DefaultConfig.Instance
        .WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend)));
