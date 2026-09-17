// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.Zensical;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Provides benchmarks for evaluating the performance of the metadata extraction process.
/// This class uses BenchmarkDotNet attributes to measure execution times and memory usage
/// for different stages of the extraction workflow inside the SourceDocParser.
/// </summary>
[System.Diagnostics.DebuggerDisplay("MetadataExtractorBenchmarks: {_scratchRoot}")]
[ShortRunJob(RuntimeMoniker.Net10_0)]
[ShortRunJob(RuntimeMoniker.Net11_0)]
[MemoryDiagnoser]
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "BenchmarkDotNet drives lifecycle via [GlobalSetup]/[GlobalCleanup]; _source releases in GlobalCleanup.")]
public class MetadataExtractorBenchmarks
{
    /// <summary>Scratch directory for the fixture's working files (cleaned per-run).</summary>
    private string _scratchRoot = string.Empty;

    /// <summary>Per-iteration output directory the emitter writes to.</summary>
    private string _outputRoot = string.Empty;

    /// <summary>Configured NuGet assembly source pointing at <see cref="_scratchRoot"/>.</summary>
    private NuGetAssemblySource _source = null!;

    /// <summary>Emitter the extractor hands the merged catalog to.</summary>
    private ZensicalDocumentationEmitter _emitter = null!;

    /// <summary>The pipeline orchestrator under test.</summary>
    private MetadataExtractor _extractor = null!;

    /// <summary>
    /// One-time setup that copies the fixture <c>nuget-packages.json</c> next
    /// to the benchmark binary, then runs a single fetch to warm the local
    /// NuGet cache so per-iteration timings exclude the network leg.
    /// </summary>
    /// <returns>A task representing the asynchronous setup.</returns>
    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        _scratchRoot = Path.Combine(Path.GetTempPath(), $"sdp-bench-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_scratchRoot);

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "nuget-packages.json"),
            Path.Combine(_scratchRoot, "nuget-packages.json"));

        var apiPath = Path.Combine(_scratchRoot, "api");
        _ = Directory.CreateDirectory(apiPath);

        _source = new(_scratchRoot, apiPath);
        _emitter = new();
        _extractor = new();

        // Warm the local cache so [Benchmark] iterations don't re-fetch.
        var warmupOutput = Path.Combine(_scratchRoot, "warmup");
        await _extractor.RunAsync(_source, new FilePageSink(warmupOutput), _emitter).ConfigureAwait(false);
    }

    /// <summary>Per-iteration setup. Allocates a fresh output directory so each run starts clean.</summary>
    [IterationSetup]
    public void IterationSetup() => _outputRoot = Path.Combine(_scratchRoot, $"iter-{Guid.NewGuid():N}");

    /// <summary>Removes the iteration's output tree before the next measurement.</summary>
    [IterationCleanup]
    public void IterationCleanup()
    {
        if (Directory.Exists(_outputRoot))
        {
            Directory.Delete(_outputRoot, recursive: true);
        }
    }

    /// <summary>Disposes the assembly source (and its shared <see cref="HttpClient"/>) and removes the scratch directory after the benchmark series completes.</summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _source.Dispose();
        if (!Directory.Exists(_scratchRoot))
        {
            return;
        }

        Directory.Delete(_scratchRoot, recursive: true);
    }

    /// <summary>Measures one full <c>RunAsync</c>: discover groups, walk every assembly, merge by UID, hand to the emitter.</summary>
    /// <returns>The extraction summary (returned so BenchmarkDotNet doesn't elide the call).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Benchmark]
    public Task<ExtractionResult> RunAsync() =>
        _extractor.RunAsync(_source, new FilePageSink(_outputRoot), _emitter);
}
