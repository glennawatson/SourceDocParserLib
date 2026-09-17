// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using SourceDocParser.LibCompilation;
using SourceDocParser.Merge;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;
using SourceDocParser.Zensical;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Represents a set of benchmarks for evaluating the performance of various
/// pipeline phases in the source document parsing process, including discovery,
/// loading and walking, merging, emitting, and source link operations.
/// </summary>
[System.Diagnostics.DebuggerDisplay("PipelinePhaseBenchmarks: {_scratchRoot}")]
[ShortRunJob(RuntimeMoniker.Net10_0)]
[ShortRunJob(RuntimeMoniker.Net11_0)]
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "BenchmarkDotNet drives lifecycle via [GlobalSetup]/[GlobalCleanup]; _source releases in GlobalCleanup.")]
public class PipelinePhaseBenchmarks
{
    /// <summary>Scratch directory the fixture lives in.</summary>
    private string _scratchRoot = string.Empty;

    /// <summary>Per-iteration output directory the emit benchmark writes to.</summary>
    private string _outputRoot = string.Empty;

    /// <summary>Configured NuGet source pointing at the warmed cache.</summary>
    private NuGetAssemblySource _source = null!;

    /// <summary>Emitter the emit benchmark feeds.</summary>
    private ZensicalDocumentationEmitter _emitter = null!;

    /// <summary>TFM groups discovered once during setup; reused by every benchmark.</summary>
    private List<AssemblyGroup> _groups = [];

    /// <summary>Catalogs produced by an upfront LoadAndWalk; consumed by <see cref="MergeBench"/>.</summary>
    private List<ApiCatalog> _walkedCatalogs = [];

    /// <summary>Merged canonical types produced by an upfront merge; consumed by <see cref="EmitBench"/>.</summary>
    private ApiType[] _mergedTypes = [];

    /// <summary>
    /// Pre-loaded compilations held alive for the lifetime of the benchmark
    /// series so <see cref="WalkOnlyBench"/> measures pure walk cost (the
    /// memory-mapped DLL views are pinned across iterations).
    /// </summary>
    private List<PreLoadedAssembly> _preLoaded = [];

    /// <summary>Loaders backing <see cref="_preLoaded"/>; held so the cached metadata references aren't released between iterations. Disposed in <see cref="GlobalCleanup"/>.</summary>
    private List<CompilationLoader> _preLoadedLoaders = [];

    /// <summary>
    /// Asynchronously sets up the required environment and dependencies for the benchmark workflow.
    /// This method initializes temporary directories, prepares necessary assets, warms up caches
    /// to exclude network and I/O delay from measured iterations, and pre-loads assemblies
    /// to ensure accurate benchmarking of the pipeline phases.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation of the setup process.</returns>
    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        _scratchRoot = Path.Combine(Path.GetTempPath(), $"sdp-phasebench-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_scratchRoot);

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "nuget-packages.json"),
            Path.Combine(_scratchRoot, "nuget-packages.json"));

        var apiPath = Path.Combine(_scratchRoot, "api");
        _ = Directory.CreateDirectory(apiPath);

        _source = new(_scratchRoot, apiPath);
        _emitter = new();

        // Warm NuGet cache so per-iteration timings exclude the network leg.
        var warmer = new MetadataExtractor();
        await warmer.RunAsync(_source, new FilePageSink(Path.Combine(_scratchRoot, "warmup")), _emitter).ConfigureAwait(false);

        // Capture discovered groups for every phase to reuse.
        _groups = [];
        await foreach (var group in _source.DiscoverAsync().ConfigureAwait(false))
        {
            _groups.Add(group);
        }

        // Capture catalogs from an upfront walk for MergeBench.
        _walkedCatalogs = BuildWalkedCatalogs(_groups);

        // Merge once so EmitBench has canonical types ready.
        _mergedTypes = TypeMerger.Merge(_walkedCatalogs);

        // Pre-load every assembly into a held-alive compilation so
        // WalkOnlyBench measures only the walker and not the loader.
        _preLoaded = [];
        _preLoadedLoaders = [];
        for (var groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
        {
            var group = _groups[groupIndex];
            var loader = new CompilationLoader();
            _preLoadedLoaders.Add(loader);
            for (var pathIndex = 0; pathIndex < group.AssemblyPaths.Length; pathIndex++)
            {
                var path = group.AssemblyPaths[pathIndex];
                try
                {
                    var (compilation, assembly) = loader.Load(path, group.FallbackIndex);
                    var sourceLinks = new SourceLinkResolver(path);
                    _preLoaded.Add(new(group.Tfm, compilation, assembly, sourceLinks));
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
                {
                    // Skip assemblies the loader can't handle, same as production.
                }
            }
        }
    }

    /// <summary>Allocates a fresh output directory per iteration so the emit phase isn't measuring directory-clear cost.</summary>
    [IterationSetup]
    public void IterationSetup() => _outputRoot = Path.Combine(_scratchRoot, $"iter-{Guid.NewGuid():N}");

    /// <summary>Removes the output tree before the next measurement.</summary>
    [IterationCleanup]
    public void IterationCleanup()
    {
        if (Directory.Exists(_outputRoot))
        {
            Directory.Delete(_outputRoot, recursive: true);
        }
    }

    /// <summary>Disposes the held-alive resolvers + loaders, then removes the scratch directory.</summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        for (var i = 0; i < _preLoaded.Count; i++)
        {
            _preLoaded[i].SourceLinks.Dispose();
        }

        for (var i = 0; i < _preLoadedLoaders.Count; i++)
        {
            _preLoadedLoaders[i].Dispose();
        }

        _source.Dispose();

        if (!Directory.Exists(_scratchRoot))
        {
            return;
        }

        Directory.Delete(_scratchRoot, recursive: true);
    }

    /// <summary>
    /// Just walks the NuGet source's <c>DiscoverAsync</c> stream -- no
    /// Roslyn, no merge, no emit. Captures the per-call cost of the
    /// owner discovery + per-package fallback-index build.
    /// </summary>
    /// <returns>The number of groups yielded.</returns>
    [Benchmark]
    public async Task<int> DiscoverBench()
    {
        var count = 0;
        await foreach (var group in _source.DiscoverAsync().ConfigureAwait(false))
        {
            _ = group;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Loads + walks every assembly across every TFM group using a fresh
    /// <see cref="CompilationLoader"/> per group (matching the production
    /// pipeline). Disposes the loader on scope exit so memory-mapped DLL
    /// views aren't pinned across iterations. Sequential -- measures the
    /// raw walker cost without parallel-dispatch overhead.
    /// </summary>
    /// <returns>The number of catalogs produced.</returns>
    [Benchmark]
    public int LoadAndWalkBench()
    {
        var walker = new SymbolWalker();
        var produced = 0;
        for (var groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
        {
            var group = _groups[groupIndex];
            using var loader = new CompilationLoader();
            for (var pathIndex = 0; pathIndex < group.AssemblyPaths.Length; pathIndex++)
            {
                var path = group.AssemblyPaths[pathIndex];
                try
                {
                    var (compilation, assembly) = loader.Load(path, group.FallbackIndex);
                    using var sourceLinks = new SourceLinkResolver(path);
                    _ = walker.Walk(group.Tfm, assembly, compilation, sourceLinks);
                    produced++;
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
                {
                    // Match production behavior: skip on load failure.
                }
            }
        }

        return produced;
    }

    /// <summary>Runs <see cref="TypeMerger.Merge"/> on the catalogs captured during setup.</summary>
    /// <returns>The number of canonical types produced.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Benchmark]
    public int MergeBench() => TypeMerger.Merge(_walkedCatalogs).Length;

    /// <summary>Hands the pre-merged canonical types to the Zensical emitter.</summary>
    /// <returns>The number of pages emitted.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Benchmark]
    public Task<int> EmitBench() => _emitter.EmitAsync(_mergedTypes, new FilePageSink(_outputRoot));

    /// <summary>Loads every assembly without walking.</summary>
    /// <returns>Number of compilations produced.</returns>
    [Benchmark]
    public int LoadOnlyBench()
    {
        var loaded = 0;
        for (var groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
        {
            var group = _groups[groupIndex];
            using var loader = new CompilationLoader();
            for (var pathIndex = 0; pathIndex < group.AssemblyPaths.Length; pathIndex++)
            {
                var path = group.AssemblyPaths[pathIndex];
                try
                {
                    _ = loader.Load(path, group.FallbackIndex);
                    loaded++;
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
                {
                    // Skip on load failure, same as production.
                }
            }
        }

        return loaded;
    }

    /// <summary>Walks every pre-loaded compilation (held alive in setup). Measures pure walker cost -- no loader, no PDB construction.</summary>
    /// <returns>Number of catalogs produced.</returns>
    [Benchmark]
    public int WalkOnlyBench()
    {
        var walker = new SymbolWalker();
        for (var i = 0; i < _preLoaded.Count; i++)
        {
            var entry = _preLoaded[i];
            _ = walker.Walk(entry.Tfm, entry.Assembly, entry.Compilation, entry.SourceLinks);
        }

        return _preLoaded.Count;
    }

    /// <summary>Constructs a fresh <see cref="SourceLinkResolver"/> per assembly path and disposes it. Measures the PDB-open cost in isolation.</summary>
    /// <returns>Number of resolvers constructed.</returns>
    [Benchmark]
    public int SourceLinkOnlyBench()
    {
        var made = 0;
        for (var groupIndex = 0; groupIndex < _groups.Count; groupIndex++)
        {
            var group = _groups[groupIndex];
            for (var pathIndex = 0; pathIndex < group.AssemblyPaths.Length; pathIndex++)
            {
                var path = group.AssemblyPaths[pathIndex];
                using var sourceLinks = new SourceLinkResolver(path);
                made++;
            }
        }

        return made;
    }

    /// <summary>Walks each discovered assembly once to prepare the merge fixture.</summary>
    /// <param name="groups">Discovered assemblies and their reference maps.</param>
    /// <returns>Catalogs of successfully loaded assemblies.</returns>
    private static List<ApiCatalog> BuildWalkedCatalogs(List<AssemblyGroup> groups)
    {
        List<ApiCatalog> catalogs = [with(groups.Count)];
        var walker = new SymbolWalker();
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            using var loader = new CompilationLoader();
            for (var pathIndex = 0; pathIndex < group.AssemblyPaths.Length; pathIndex++)
            {
                var path = group.AssemblyPaths[pathIndex];
                try
                {
                    var (compilation, assembly) = loader.Load(path, group.FallbackIndex);
                    using var sourceLinks = new SourceLinkResolver(path);
                    catalogs.Add(walker.Walk(group.Tfm, assembly, compilation, sourceLinks));
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
                {
                    // Skip unsupported or unreadable assemblies.
                }
            }
        }

        return catalogs;
    }

    /// <summary>One pre-loaded assembly held alive across the benchmark series so <see cref="WalkOnlyBench"/> doesn't pay the load cost per iteration.</summary>
    /// <param name="Tfm">TFM the assembly was loaded under.</param>
    /// <param name="Compilation">The Roslyn compilation hosting the assembly.</param>
    /// <param name="Assembly">The primary assembly symbol.</param>
    /// <param name="SourceLinks">Resolver scoped to the assembly; disposed in <see cref="GlobalCleanup"/>.</param>
    private sealed record PreLoadedAssembly(
        string Tfm,
        Microsoft.CodeAnalysis.Compilation Compilation,
        Microsoft.CodeAnalysis.IAssemblySymbol Assembly,
        SourceLinkResolver SourceLinks);
}
