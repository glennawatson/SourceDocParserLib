// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using SourceDocParser.LibCompilation;
using SourceDocParser.Merge;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;
using SourceDocParser.Zensical;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Per-phase benchmarks that split <see cref="MetadataExtractor.RunAsync"/>
/// into its constituent steps so we can attribute the end-to-end allocation
/// budget (currently ~3.65 GB on the slim debug fixture) to a specific phase.
/// Every phase shares a single warmed NuGet cache and pre-discovered group
/// list captured during <see cref="GlobalSetupAsync"/>; <see cref="MergeBench"/>
/// and <see cref="EmitBench"/> additionally consume artifacts produced by an
/// initial walk + merge so each iteration measures only that phase.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
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

    /// <summary>
    /// Loaders backing <see cref="_preLoaded"/>; held so the cached
    /// metadata references aren't released between iterations.
    /// Disposed in <see cref="GlobalCleanup"/>.
    /// </summary>
    private List<CompilationLoader> _preLoadedLoaders = [];

    /// <summary>
    /// Warms the NuGet cache via a single <see cref="MetadataExtractor.RunAsync"/>
    /// then captures (a) discovered groups, (b) walked catalogs, (c) merged types
    /// so each phase benchmark exercises only its own step.
    /// </summary>
    /// <returns>A task representing the asynchronous setup.</returns>
    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        _scratchRoot = Path.Combine(Path.GetTempPath(), $"sdp-phasebench-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchRoot);

        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "nuget-packages.json"),
            Path.Combine(_scratchRoot, "nuget-packages.json"));

        var apiPath = Path.Combine(_scratchRoot, "api");
        Directory.CreateDirectory(apiPath);

        _source = new(_scratchRoot, apiPath);
        _emitter = new();

        // Warm NuGet cache so per-iteration timings exclude the network leg.
        var warmer = new MetadataExtractor();
        await warmer.RunAsync(_source, Path.Combine(_scratchRoot, "warmup"), _emitter).ConfigureAwait(false);

        // Capture discovered groups for every phase to reuse.
        _groups = [];
        await foreach (var group in _source.DiscoverAsync().ConfigureAwait(false))
        {
            _groups.Add(group);
        }

        // Capture catalogs from an upfront walk for MergeBench.
        _walkedCatalogs = [];
        var walker = new SymbolWalker();
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
                    _walkedCatalogs.Add(walker.Walk(group.Tfm, assembly, compilation, sourceLinks));
                }
                catch
                {
                    // Match production behavior: skip on load failure.
                }
            }
        }

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
                catch
                {
                    // Skip assemblies the loader can't handle, same as production.
                }
            }
        }
    }

    /// <summary>Allocates a fresh output directory per iteration so the emit phase isn't measuring directory-clear cost.</summary>
    [IterationSetup]
    public void IterationSetup() => _outputRoot = Path.Combine(_scratchRoot, $"iter-{Guid.NewGuid():N}");

    /// <summary>
    /// Per-iteration cleanup. Drops the output tree (if any) and forces a
    /// full GC pass so memory-mapped DLL views and Roslyn compilation state
    /// from the LoadAndWalk benchmark don't accumulate across iterations.
    /// </summary>
    [IterationCleanup]
    public void IterationCleanup()
    {
        if (Directory.Exists(_outputRoot))
        {
            Directory.Delete(_outputRoot, recursive: true);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Disposes the held-alive resolvers + loaders, then removes the
    /// scratch directory.
    /// </summary>
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

        if (!Directory.Exists(_scratchRoot))
        {
            return;
        }

        Directory.Delete(_scratchRoot, recursive: true);
    }

    /// <summary>
    /// Just walks the NuGet source's <c>DiscoverAsync</c> stream — no
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
    /// views aren't pinned across iterations. Sequential — measures the
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
                    walker.Walk(group.Tfm, assembly, compilation, sourceLinks);
                    produced++;
                }
                catch
                {
                    // Match production behavior: skip on load failure.
                }
            }
        }

        return produced;
    }

    /// <summary>Runs <see cref="TypeMerger.Merge"/> on the catalogs captured during setup.</summary>
    /// <returns>The number of canonical types produced.</returns>
    [Benchmark]
    public int MergeBench() => TypeMerger.Merge(_walkedCatalogs).Length;

    /// <summary>Hands the pre-merged canonical types to the Zensical emitter.</summary>
    /// <returns>The number of pages emitted.</returns>
    [Benchmark]
    public Task<int> EmitBench() => _emitter.EmitAsync(_mergedTypes, _outputRoot);

    /// <summary>
    /// Loads every assembly without walking. Splits the
    /// <see cref="LoadAndWalkBench"/> budget so we can attribute it to
    /// <see cref="CompilationLoader.Load"/> versus <see cref="SymbolWalker.Walk"/>.
    /// </summary>
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
                    loader.Load(path, group.FallbackIndex);
                    loaded++;
                }
                catch
                {
                    // Skip on load failure, same as production.
                }
            }
        }

        return loaded;
    }

    /// <summary>
    /// Walks every pre-loaded compilation (held alive in setup). Measures
    /// pure walker cost — no loader, no PDB construction.
    /// </summary>
    /// <returns>Number of catalogs produced.</returns>
    [Benchmark]
    public int WalkOnlyBench()
    {
        var walker = new SymbolWalker();
        for (var i = 0; i < _preLoaded.Count; i++)
        {
            var entry = _preLoaded[i];
            walker.Walk(entry.Tfm, entry.Assembly, entry.Compilation, entry.SourceLinks);
        }

        return _preLoaded.Count;
    }

    /// <summary>
    /// Constructs a fresh <see cref="SourceLinkResolver"/> per assembly
    /// path and disposes it. Measures the PDB-open cost in isolation.
    /// </summary>
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

    /// <summary>
    /// One pre-loaded assembly held alive across the benchmark series so
    /// <see cref="WalkOnlyBench"/> doesn't pay the load cost per iteration.
    /// </summary>
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
