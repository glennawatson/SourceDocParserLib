# NuGet package fetching

[Performance overview](../../docs/performance.md) · [Benchmark guide](README.md)

This comparison measures package discovery and reference acquisition for Refit
15.2.0 on `net10.0`, with Microsoft.NETCore.App.Ref pinned to 10.0.0. Downloaded
package caches are warm. Fresh-source runs regenerate the graph output.

| Scenario | Published SourceDocParser 2.2.0 | NuGet restore pipeline | Managed allocation, 2.2.0 → restore |
|---|---:|---:|---:|
| Reused source and graph | 2,088 ± 8.4 ms | 31.56 ± 2.616 ms | 83.86 → 13.80 MiB |
| Fresh source and graph | 4,978 ± 35.5 ms | 25.83 ± 1.663 ms | 89.96 → 14.01 MiB |

Every operation verifies one documentation root. The implementations select
different reference sets: 314 entries for 2.2.0 and 171 for the NuGet restore
pipeline. Cached and fresh discovery produce identical graph and assembly
hashes within each implementation. The measurements compare complete fetching
operations for the same root, not identical dependency-processing algorithms.

The 99.9% confidence intervals come from BenchmarkDotNet runs with five warmups
and 15 measurements on .NET 11, Release, Ryzen 7 5800X, seven physical cores,
the performance governor, and priority -20. Profiling runs separately from
timing. Managed allocations measure total allocation during the operation,
not peak memory usage.

The repository's `NuGetFetchBenchmarks` uses automatic framework-pack selection.
The paired comparison adds the same explicit reference-pack pin to both
implementations. Package-version pins are optional in normal documentation
configuration.
