// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using SourceDocParser.NuGet;

namespace SourceDocParser.Docfx.StandaloneBenchmarks;

/// <summary>
/// Equivalent benchmark for the SourceDocParser.Docfx YAML emitter —
/// the apples-to-apples partner of Docfx.StandaloneBenchmarks. One
/// <c>[Params]</c>-selected TFM per row keeps each measurement scoped
/// to a single framework slice so the side-by-side comparison runs on
/// the same physical assemblies on both sides.
/// </summary>
[MemoryDiagnoser]
public class SourceDocParserDocfxLibraryBenchmark
{
    /// <summary>Scratch directory holding per-TFM scratch + warmed cache.</summary>
    private string _scratchRoot = string.Empty;

    /// <summary>Per-TFM configured assembly source — populated in GlobalSetup, indexed by short TFM.</summary>
    private Dictionary<string, NuGetAssemblySource> _sourcePerTfm = new(StringComparer.Ordinal);

    /// <summary>Per-iteration output directory.</summary>
    private string _outputRoot = string.Empty;

    /// <summary>Source for the current iteration's <see cref="Tfm"/>.</summary>
    private NuGetAssemblySource _source = null!;

    /// <summary>Docfx YAML emitter under test.</summary>
    private DocfxYamlEmitter _emitter = null!;

    /// <summary>The pipeline orchestrator.</summary>
    private MetadataExtractor _extractor = null!;

    /// <summary>Gets or sets the TFM under measurement — BDN runs one row per value.</summary>
    [Params("net8.0", "net9.0", "net10.0", "net472")]
    public string Tfm { get; set; } = string.Empty;

    /// <summary>Gets the scratch root directory — exposed so the dump-mode runner can point users at the emitted YAML for diffing.</summary>
    public string ScratchRootForInspection => _scratchRoot;

    /// <summary>Per-TFM scaffold + cache warmup.</summary>
    /// <returns>A task representing the asynchronous setup.</returns>
    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        _scratchRoot = Path.Combine(Path.GetTempPath(), $"sdp-docfx-ours-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchRoot);

        _emitter = new();
        _extractor = new();
        _sourcePerTfm = new(StringComparer.Ordinal);

        string[] tfms = ["net8.0", "net9.0", "net10.0", "net472"];
        for (var i = 0; i < tfms.Length; i++)
        {
            var tfm = tfms[i];
            var tfmRoot = Path.Combine(_scratchRoot, tfm);
            Directory.CreateDirectory(tfmRoot);

            await File.WriteAllTextAsync(
                Path.Combine(tfmRoot, "nuget-packages.json"),
                BuildFixtureConfig(tfm)).ConfigureAwait(false);

            var apiPath = Path.Combine(tfmRoot, "api");
            Directory.CreateDirectory(apiPath);

            var source = new NuGetAssemblySource(tfmRoot, apiPath);
            _sourcePerTfm[tfm] = source;

            var warmupOutput = Path.Combine(tfmRoot, "warmup");
            await _extractor.RunAsync(source, warmupOutput, _emitter).ConfigureAwait(false);
        }
    }

    /// <summary>Per-iteration setup. Picks the source for the current <see cref="Tfm"/> and a fresh output dir.</summary>
    [IterationSetup]
    public void IterationSetup()
    {
        _source = _sourcePerTfm[Tfm];
        _outputRoot = Path.Combine(_scratchRoot, Tfm, $"iter-{Guid.NewGuid():N}");
    }

    /// <summary>Per-iteration cleanup. Drops output tree and forces GC.</summary>
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

    /// <summary>Removes the entire scratch directory after the benchmark series completes.</summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (!Directory.Exists(_scratchRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(_scratchRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>Times one full pipeline pass for the chosen TFM: discover, walk, merge, emit docfx YAML.</summary>
    /// <returns>The extraction result (returned so BDN doesn't elide the call).</returns>
    [Benchmark]
    public Task<ExtractionResult> RunAsync() =>
        _extractor.RunAsync(_source, _outputRoot, _emitter);

    /// <summary>Builds a single-TFM nuget-packages.json. Drops every other TFM so the walk is scoped to one slice.</summary>
    /// <param name="tfm">Short TFM identifier (net8.0, net472, ...).</param>
    /// <returns>The JSON config text.</returns>
    private static string BuildFixtureConfig(string tfm)
    {
        var refPackages = tfm switch
        {
            "net10.0" => """{ "id": "Microsoft.NETCore.App.Ref", "version": "10.0.0", "targetTfm": "net10.0", "pathPrefix": "ref/net10.0" }""",
            "net9.0" => """{ "id": "Microsoft.NETCore.App.Ref", "version": "9.0.0",  "targetTfm": "net9.0",  "pathPrefix": "ref/net9.0" }""",
            "net8.0" => """{ "id": "Microsoft.NETCore.App.Ref", "version": "8.0.0",  "targetTfm": "net8.0",  "pathPrefix": "ref/net8.0" }""",
            "net472" => """{ "id": "Microsoft.NETFramework.ReferenceAssemblies.net472", "targetTfm": "net472", "pathPrefix": "build/.NETFramework/v4.7.2" }""",
            _ => throw new ArgumentOutOfRangeException(nameof(tfm), tfm, "Unsupported TFM in benchmark."),
        };

        return $$"""
            {
              "nugetPackageOwners": [],
              "tfmPreference": [ "{{tfm}}" ],
              "additionalPackages": [
                { "id": "ReactiveUI" },
                { "id": "Splat" },
                { "id": "DynamicData" },
                { "id": "System.Reactive" }
              ],
              "excludePackages": [],
              "excludePackagePrefixes": [],
              "referencePackages": [ {{refPackages}} ],
              "tfmOverrides": {}
            }
            """;
    }
}
