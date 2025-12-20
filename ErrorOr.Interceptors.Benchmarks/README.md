# ErrorOr.Interceptors.Benchmarks

Benchmarks focused on interceptor generator hot paths and allocation behavior.

## What this measures

- `IsMapInvocation` predicate throughput over invocation nodes
- `GroupCallSites` performance with 100+ call sites
- `CodeBuilder` allocation/throughput in hot loops

## Project structure

```
ErrorOr.Interceptors.Benchmarks/
├── Program.cs                 # Entry point
├── InterceptorBenchmarks.cs   # Generator hot-path benchmarks
└── README.md
```

## Running

```bash
dotnet run -c Release --project ErrorOr.Interceptors.Benchmarks
```

Release mode is required for accurate measurements.
