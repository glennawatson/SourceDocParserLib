# Benchmark results

[Performance overview](../../docs/performance.md) · [Running benchmarks](README.md)

AMD Ryzen 7 5800X, Linux, seven physical cores, Release mode, BenchmarkDotNet
0.16.0-preview, five warmups and 15 measurements. Separate native jobs run on
.NET 10.0.12 and .NET 11 release candidate. Timing runs have no profiler attached.
Errors are 99.9% confidence-interval margins; allocations are total managed
allocation per operation, not peak resident memory.

The pipeline fixture pins ReactiveUI 24.2.0, Splat 21.0.0, DynamicData 9.4.33,
and System.Reactive 7.0.0. It produces 35 package/TFM groups, 462 merged types,
and 2,905 Markdown pages using warm package caches. Phase benchmarks use
prepared inputs and are not additive to the parallel complete pipeline.
Emitter and helper benchmarks use their individual synthetic fixtures.

## EmitterBenchmarks

| Method | Runtime | Parameters | Mean ± 99.9% confidence interval | Allocated |
|---|---|---|---:|---:|
|ZensicalMarkdown|.NET 10.0|TypeCount=100&MembersPerType=5|89.081 ± 17.505 µs|288.281 KiB|
|DocfxYaml|.NET 10.0|TypeCount=100&MembersPerType=5|471.653 ± 3.944 µs|1.334 MiB|
|ZensicalMarkdown|.NET 11.0|TypeCount=100&MembersPerType=5|69.111 ± 0.753 µs|288.281 KiB|
|DocfxYaml|.NET 11.0|TypeCount=100&MembersPerType=5|459.468 ± 3.513 µs|1.334 MiB|
|ZensicalMarkdown|.NET 10.0|TypeCount=100&MembersPerType=30|228.512 ± 1.177 µs|763.281 KiB|
|DocfxYaml|.NET 10.0|TypeCount=100&MembersPerType=30|2.098 ± 0.026 ms|6.189 MiB|
|ZensicalMarkdown|.NET 11.0|TypeCount=100&MembersPerType=30|231.459 ± 2.85 µs|763.281 KiB|
|DocfxYaml|.NET 11.0|TypeCount=100&MembersPerType=30|2.143 ± 0.021 ms|6.188 MiB|
|ZensicalMarkdown|.NET 10.0|TypeCount=600&MembersPerType=5|410.899 ± 7.556 µs|1.689 MiB|
|DocfxYaml|.NET 10.0|TypeCount=600&MembersPerType=5|2.956 ± 0.073 ms|8.006 MiB|
|ZensicalMarkdown|.NET 11.0|TypeCount=600&MembersPerType=5|424.298 ± 6.378 µs|1.689 MiB|
|DocfxYaml|.NET 11.0|TypeCount=600&MembersPerType=5|2.839 ± 0.118 ms|8.002 MiB|
|ZensicalMarkdown|.NET 10.0|TypeCount=600&MembersPerType=30|1.402 ± 0.023 ms|4.472 MiB|
|DocfxYaml|.NET 10.0|TypeCount=600&MembersPerType=30|13.54 ± 0.136 ms|37.134 MiB|
|ZensicalMarkdown|.NET 11.0|TypeCount=600&MembersPerType=30|1.406 ± 0.025 ms|4.472 MiB|
|DocfxYaml|.NET 11.0|TypeCount=600&MembersPerType=30|13.381 ± 0.171 ms|37.129 MiB|
|ZensicalMarkdown|.NET 10.0|TypeCount=2000&MembersPerType=5|1.502 ± 0.02 ms|5.63 MiB|
|DocfxYaml|.NET 10.0|TypeCount=2000&MembersPerType=5|10.436 ± 0.08 ms|26.688 MiB|
|ZensicalMarkdown|.NET 11.0|TypeCount=2000&MembersPerType=5|1.408 ± 0.021 ms|5.63 MiB|
|DocfxYaml|.NET 11.0|TypeCount=2000&MembersPerType=5|10.119 ± 0.119 ms|26.672 MiB|
|ZensicalMarkdown|.NET 10.0|TypeCount=2000&MembersPerType=30|6.096 ± 0.045 ms|14.908 MiB|
|DocfxYaml|.NET 10.0|TypeCount=2000&MembersPerType=30|46.74 ± 0.682 ms|123.779 MiB|
|ZensicalMarkdown|.NET 11.0|TypeCount=2000&MembersPerType=30|5.838 ± 0.069 ms|14.908 MiB|
|DocfxYaml|.NET 11.0|TypeCount=2000&MembersPerType=30|43.869 ± 0.27 ms|123.764 MiB|

## MetadataExtractorBenchmarks

| Method | Runtime | Parameters | Mean ± 99.9% confidence interval | Allocated |
|---|---|---|---:|---:|
|RunAsync|.NET 10.0||8.713 ± 0.431 s|9.849 GiB|
|RunAsync|.NET 11.0||8.737 ± 0.194 s|9.848 GiB|

## PipelinePhaseBenchmarks

| Method | Runtime | Parameters | Mean ± 99.9% confidence interval | Allocated |
|---|---|---|---:|---:|
|DiscoverBench|.NET 10.0||4.789 ± 0.633 s|1.034 GiB|
|LoadAndWalkBench|.NET 10.0||5.291 ± 0.101 s|8.779 GiB|
|MergeBench|.NET 10.0||973.691 ± 23.738 µs|403.164 KiB|
|EmitBench|.NET 10.0||63.24 ± 0.748 ms|26.981 MiB|
|LoadOnlyBench|.NET 10.0||3.165 ± 0.026 s|7.852 GiB|
|WalkOnlyBench|.NET 10.0||529.826 ± 4.832 ms|334.619 MiB|
|SourceLinkOnlyBench|.NET 10.0||7.669 ± 0.082 ms|369.75 KiB|
|DiscoverBench|.NET 11.0||4.719 ± 0.398 s|1.034 GiB|
|LoadAndWalkBench|.NET 11.0||5.832 ± 0.407 s|8.78 GiB|
|MergeBench|.NET 11.0||1.079 ± 0.017 ms|399.563 KiB|
|EmitBench|.NET 11.0||60.404 ± 0.168 ms|27.047 MiB|
|LoadOnlyBench|.NET 11.0||3.063 ± 0.044 s|7.852 GiB|
|WalkOnlyBench|.NET 11.0||492.201 ± 2.982 ms|334.694 MiB|
|SourceLinkOnlyBench|.NET 11.0||7.512 ± 0.012 ms|370.57 KiB|

## TfmResolverBenchmarks

| Method | Runtime | Parameters | Mean ± 99.9% confidence interval | Allocated |
|---|---|---|---:|---:|
|FindBestRefsTfmExactMatch|.NET 10.0||3.576 ± 0.012 ns|0 B|
|FindBestRefsTfmPlatformSuffix|.NET 10.0||10.754 ± 0.052 ns|0 B|
|FindBestRefsTfmNetstandardFallback|.NET 10.0||440.58 ± 2.9 ns|1.109 KiB|
|FindBestRefsTfmFrameworkRefs|.NET 10.0||4.219 ± 0.023 ns|0 B|
|FindBestRefsTfmMixedRefs|.NET 10.0||5.43 ± 0.007 ns|0 B|
|FindBestRefsTfmExactMatch|.NET 11.0||3.566 ± 0.014 ns|0 B|
|FindBestRefsTfmPlatformSuffix|.NET 11.0||9.866 ± 0.065 ns|0 B|
|FindBestRefsTfmNetstandardFallback|.NET 11.0||443.937 ± 3.259 ns|1.086 KiB|
|FindBestRefsTfmFrameworkRefs|.NET 11.0||4.231 ± 0.004 ns|0 B|
|FindBestRefsTfmMixedRefs|.NET 11.0||5.479 ± 0.005 ns|0 B|

## TypeMergerBenchmarks

| Method | Runtime | Parameters | Mean ± 99.9% confidence interval | Allocated |
|---|---|---|---:|---:|
|Merge|.NET 10.0|TypeCount=100|26.364 ± 0.111 µs|171.397 KiB|
|Merge|.NET 11.0|TypeCount=100|24.766 ± 0.172 µs|170.616 KiB|
|Merge|.NET 10.0|TypeCount=600|114.679 ± 0.83 µs|362.804 KiB|
|Merge|.NET 11.0|TypeCount=600|101.272 ± 0.51 µs|358.116 KiB|
|Merge|.NET 10.0|TypeCount=2000|507.676 ± 5.223 µs|898.741 KiB|
|Merge|.NET 11.0|TypeCount=2000|467.505 ± 5.065 µs|883.116 KiB|

## XmlDocToMarkdownBenchmarks

| Method | Runtime | Parameters | Mean ± 99.9% confidence interval | Allocated |
|---|---|---|---:|---:|
|ConvertPlainSummary|.NET 10.0||22.421 ± 0.385 ns|176 B|
|ConvertTaggedSummary|.NET 10.0||793.642 ± 2.885 ns|456 B|
|ConvertCodeAndListSummary|.NET 10.0||1.037 ± 0.005 µs|440 B|
|ConvertPlainSummary|.NET 11.0||21.953 ± 0.301 ns|176 B|
|ConvertTaggedSummary|.NET 11.0||651.458 ± 3.848 ns|456 B|
|ConvertCodeAndListSummary|.NET 11.0||771.516 ± 4.945 ns|440 B|

