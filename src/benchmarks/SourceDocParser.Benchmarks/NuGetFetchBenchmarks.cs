// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.Benchmarks;

/// <summary>Measures complete NuGet discovery with populated package caches and either reused or fresh source inputs and graph output.</summary>
[System.Diagnostics.DebuggerDisplay("NuGetFetchBenchmarks: {FreshSource}")]
[Config(typeof(FetchJobConfig))]
[MemoryDiagnoser]
public class NuGetFetchBenchmarks
{
    /// <summary>The reproducible documentation manifest shared by both graph-cache scenarios.</summary>
    private const string PackageManifest = """
        {
          "nugetPackageOwners": [],
          "tfmPreference": ["net10.0"],
          "additionalPackages": [{"id": "Refit", "version": "15.2.0"}],
          "excludePackages": [],
          "excludePackagePrefixes": [],
          "referencePackages": [],
          "tfmOverrides": {"Refit": "net10.0"}
        }
        """;

    /// <summary>Generated graph outputs; downloaded package archives live in a separate cache directory.</summary>
    private static readonly string[] GraphDirectories = ["lib", "refs", "restore"];

    /// <summary>Working directory owned exclusively by this benchmark instance.</summary>
    private string _fixtureDirectory = string.Empty;

    /// <summary>The current input directory containing the package manifest.</summary>
    private string _rootDirectory = string.Empty;

    /// <summary>Destination for fetch outputs and graph files.</summary>
    private string _apiPath = string.Empty;

    /// <summary>The complete fixture workload expected in every measured operation.</summary>
    private DiscoveryCounts _expectedCounts;

    /// <summary>Gets or sets whether each operation uses fresh source inputs, SDK evaluation, and graph output.</summary>
    [Params(false, true)]
    public bool FreshSource { get; set; }

    /// <summary>Gets the discovered group, root-assembly, and reference-assembly counts.</summary>
    public DiscoveryCounts Counts { get; private set; }

    /// <summary>Creates the fixed package input and populates package caches before measuring discovery.</summary>
    /// <returns>The asynchronous setup operation.</returns>
    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        _fixtureDirectory = Path.Combine(Path.GetTempPath(), $"sdp-nuget-fetch-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_fixtureDirectory);
        _apiPath = Path.Combine(_fixtureDirectory, "api");
        PrepareSource();
        Counts = await DiscoverAsync().ConfigureAwait(false);
        _expectedCounts = Counts;
    }

    /// <summary>Refreshes source inputs and graph output while retaining downloaded package archives.</summary>
    [IterationSetup]
    public void PrepareGraph()
    {
        if (!FreshSource)
        {
            return;
        }

        PrepareSource();
        for (var i = 0; i < GraphDirectories.Length; i++)
        {
            var directory = Path.Combine(_apiPath, GraphDirectories[i]);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        File.Delete(Path.Combine(_apiPath, ".primary-packages"));
        foreach (var manifest in Directory.EnumerateFiles(_apiPath, ".restored-assemblies-*.json"))
        {
            File.Delete(manifest);
        }

        var cache = Path.Combine(_apiPath, "cache");
        if (!Directory.Exists(cache))
        {
            return;
        }

        foreach (var marker in Directory.EnumerateFiles(cache, "*.nupkg.nuspec"))
        {
            File.Delete(marker);
        }
    }

    /// <summary>Fetches the fixed root and fully consumes its assembly-discovery stream.</summary>
    /// <returns>The group and assembly counts produced by the operation.</returns>
    /// <exception cref="InvalidOperationException">Discovery does not return the complete pinned Refit workload.</exception>
    [Benchmark]
    public async Task<DiscoveryCounts> DiscoverAsync()
    {
        using var source = new NuGetAssemblySource(_rootDirectory, _apiPath);
        var groups = 0;
        var roots = 0;
        var references = 0;
        var matchesRoot = true;
        await foreach (var group in source.DiscoverAsync().ConfigureAwait(false))
        {
            groups++;
            roots += group.AssemblyPaths.Length;
            references += group.FallbackIndex.Count;
            matchesRoot &= group.Tfm.Equals("net10.0", StringComparison.Ordinal);
            for (var i = 0; i < group.AssemblyPaths.Length; i++)
            {
                matchesRoot &= Path.GetFileNameWithoutExtension(group.AssemblyPaths[i]).Equals("Refit", StringComparison.OrdinalIgnoreCase);
            }
        }

        Counts = new(groups, roots, references);
        if (groups != 1 || roots != 1 || references is 0 || !matchesRoot || (_expectedCounts.Groups is not 0 && Counts != _expectedCounts))
        {
            throw new InvalidOperationException($"Discovery did not preserve the Refit 15.2.0/net10.0 workload: {Counts}; expected {_expectedCounts}.");
        }

        return Counts;
    }

    /// <summary>Reports workload counts and removes only the benchmark-owned working directory.</summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        ConsoleLogger.Default.WriteLine(LogKind.Info, $"Refit 15.2.0; net10.0; fresh source and graph={FreshSource}; package caches=warm; {Counts}");
        if (Directory.Exists(_fixtureDirectory))
        {
            Directory.Delete(_fixtureDirectory, recursive: true);
        }
    }

    /// <summary>Creates an independent manifest input directory without changing package-cache locations.</summary>
    private void PrepareSource()
    {
        _rootDirectory = Path.Combine(_fixtureDirectory, $"input-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(_rootDirectory);
        File.WriteAllText(Path.Combine(_rootDirectory, "nuget-packages.json"), PackageManifest);
    }

    /// <summary>Discovery workload counts used to interpret timing and allocation results.</summary>
    /// <param name="Groups">Number of returned target-framework groups.</param>
    /// <param name="RootAssemblies">Assemblies selected for documentation pages.</param>
    /// <param name="ReferenceAssemblies">Selected assembly references summed across groups.</param>
    [System.Diagnostics.DebuggerDisplay("{ToString(),nq}")]
    public readonly record struct DiscoveryCounts(int Groups, int RootAssemblies, int ReferenceAssemblies);

    /// <summary>Runs the same managed host for published-package and checkout comparisons.</summary>
    public sealed class FetchJobConfig : ManualConfig
    {
        /// <summary>Warmups permit tiered compilation before measurement.</summary>
        private const int WarmupIterations = 5;

        /// <summary>Repeated measurements expose scheduling and I/O variance.</summary>
        private const int MeasurementIterations = 15;

        /// <summary>Initializes a new instance of the <see cref="FetchJobConfig"/> class.</summary>
        public FetchJobConfig() => AddJob(Job.Default
            .WithToolchain(InProcessEmitToolchain.Default)
            .WithWarmupCount(WarmupIterations)
            .WithIterationCount(MeasurementIterations)
            .WithInvocationCount(1)
            .WithUnrollFactor(1));
    }
}
