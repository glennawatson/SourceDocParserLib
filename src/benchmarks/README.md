# Performance

For an overview aimed at documentation authors, see
[`docs/performance.md`](../../docs/performance.md).

SourceDocParser measures package acquisition, API extraction, merging, and
emission as separate workloads. Its primary target is API documentation for
MkDocs / Zensical, with a Docfx YAML adapter for integration with Docfx sites.

- [Pipeline, helper, and emitter measurements](results.md)
- [NuGet package-fetch comparison](nuget-fetching.md)
- [SourceDocParser and Docfx 2.80.1](docfx-comparison.md)

The comparison notes explain the products' different scopes, matched inputs,
and output differences. Results are workload-specific; API extraction timings
do not describe complete website builds or establish feature parity.

## Running the harness

Run commands from `src/` so the repository SDK and package configuration apply.
The pipeline fixture pins its package roots in
[`SourceDocParser.Benchmarks/Fixtures/nuget-packages.json`](SourceDocParser.Benchmarks/Fixtures/nuget-packages.json).

```bash
dotnet run --project benchmarks/SourceDocParser.Benchmarks \
  --framework net11.0 --configuration Release -- \
  --filter '*MetadataExtractorBenchmarks*' '*PipelinePhaseBenchmarks*' \
           '*EmitterBenchmarks*' '*TypeMergerBenchmarks*' \
           '*TfmResolverBenchmarks*' '*XmlDocToMarkdownBenchmarks*' \
  --warmupCount 5 --iterationCount 15
```

The configured jobs measure .NET 10 and .NET 11 independently. Allocation
profiling runs separately through `PipelinePhaseAllocationBenchmarks` and
`NuGetFetchAllocationBenchmarks`; profiled timings are not used for comparisons.

The standalone `SourceDocParser.Docfx.StandaloneBenchmarks` and
`Docfx.StandaloneBenchmarks` projects exercise YAML generation. Their default
multi-package fixture differs from the pinned Refit comparison. Align package
versions, target frameworks, reference files, visibility settings, and emitted
API inventories before comparing another workload.
