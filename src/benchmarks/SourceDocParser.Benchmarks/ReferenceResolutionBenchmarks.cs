// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;
using BenchmarkDotNet.Attributes;
using SourceDocParser.LibCompilation;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Pins the per-call cost of the reference-resolution helpers added
/// alongside the SDK ref-pack discovery work. Each benchmark stays
/// in its own method so it shows up as a distinct row in the BDN
/// report -- the goal is to spot regressions on individual fast
/// paths rather than measure them as an aggregate.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class ReferenceResolutionBenchmarks
{
    /// <summary>The Uno-style synthetic-stub version sentinel.</summary>
    private const int StubVersionMax = 255;

    /// <summary>Compatible TFMs the probe considers per call.</summary>
    private static readonly string[] CompatibleTfms = ["net8.0", "net6.0", "netstandard2.1", "netstandard2.0"];

    /// <summary>Pre-built stub Version reused across every iteration of <c>FilterStubVersion</c>.</summary>
    private static readonly Version StubVersion = new(StubVersionMax, StubVersionMax, StubVersionMax, StubVersionMax);

    /// <summary>
    /// Live snapshot used by the locator benchmarks. Captured once at
    /// global setup; the tested method never mutates it.
    /// </summary>
    private DotNetSdkLocatorInputs _locatorInputs;

    /// <summary>Scratch directory holding the synthetic pack tree.</summary>
    private string _packsScratchRoot = null!;

    /// <summary>Pack roots passed to <see cref="RefPackProbe.ProbeRefPackRefDirs"/>.</summary>
    private string[] _packRoots = null!;

    /// <summary>Realistic mix of unresolved-ref names for the <see cref="KnownFrameworkPackageMap"/> bench.</summary>
    private string[] _mixedRefNames = null!;

    /// <summary>
    /// Builds the synthetic pack tree once per process so the disk-
    /// driven probe benchmarks measure pure scan cost (no fixture
    /// rebuild on every iteration).
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _packsScratchRoot = Path.Combine(
            Path.GetTempPath(),
            "sdp-bench-packs-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_packsScratchRoot);

        var packNames = new[]
        {
            "Microsoft.NETCore.App.Ref",
            "Microsoft.WindowsDesktop.App.Ref",
            "Microsoft.AspNetCore.App.Ref",
            "Microsoft.Android.Ref.34",
        };
        var versions = new[] { "8.0.5", "8.0.10", "8.0.20" };
        var tfms = new[] { "net8.0", "net6.0" };

        for (var p = 0; p < packNames.Length; p++)
        {
            for (var v = 0; v < versions.Length; v++)
            {
                for (var t = 0; t < tfms.Length; t++)
                {
                    Directory.CreateDirectory(Path.Combine(
                        _packsScratchRoot,
                        packNames[p],
                        versions[v],
                        "ref",
                        tfms[t]));
                }
            }
        }

        _packRoots = [_packsScratchRoot];

        _mixedRefNames =
        [
            "Microsoft.WinUI",
            "WinRT.Runtime",
            "Microsoft.Web.WebView2.Core",
            "System.Reactive",
            "Splat",
            "Microsoft.InteractiveExperiences.Projection",
            "Newtonsoft.Json",
            "Microsoft.Web.WebView2.Wpf",
        ];

        _locatorInputs = DotNetSdkLocatorInputs.Snapshot();
    }

    /// <summary>Tears down the synthetic pack tree.</summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (!Directory.Exists(_packsScratchRoot))
        {
            return;
        }

        Directory.Delete(_packsScratchRoot, recursive: true);
    }

    /// <summary>
    /// Hot path: a known-platform exact name match. Called per
    /// transitive reference during compilation -- has to be cheap.
    /// </summary>
    /// <returns>Filter result.</returns>
    [Benchmark(Baseline = true)]
    public bool FilterExactMatch() => UnresolvableReferenceFilter.IsKnownUnresolvableName("Microsoft.WinUI");

    /// <summary>Prefix-match path -- slightly slower than exact since the dict misses first.</summary>
    /// <returns>Filter result.</returns>
    [Benchmark]
    public bool FilterPrefixMatch() => UnresolvableReferenceFilter.IsKnownUnresolvableName("Microsoft.Maui.Controls");

    /// <summary>Negative path -- a real NuGet package name; the most common case.</summary>
    /// <returns>Filter result.</returns>
    [Benchmark]
    public bool FilterNoMatch() => UnresolvableReferenceFilter.IsKnownUnresolvableName("System.Reactive");

    /// <summary>Stub-version sentinel detection (255.255.255.255 / 0.0.0.0).</summary>
    /// <returns>Filter result.</returns>
    [Benchmark]
    public bool FilterStubVersion() => UnresolvableReferenceFilter.IsStubVersion(StubVersion);

    /// <summary>Synthetic-ref → NuGet-package map: a hit on Microsoft.WindowsAppSDK.</summary>
    /// <returns>Mapped package id, or null.</returns>
    [Benchmark]
    public string? PackageMapHit() => KnownFrameworkPackageMap.TryGetPackageId("Microsoft.WinUI");

    /// <summary>Synthetic-ref map miss for a real NuGet package.</summary>
    /// <returns>Mapped package id, or null.</returns>
    [Benchmark]
    public string? PackageMapMiss() => KnownFrameworkPackageMap.TryGetPackageId("System.Reactive");

    /// <summary>
    /// AdditionalNuGetPackagesFor with a realistic mix of mapped and
    /// unmapped refs -- exercises the dedupe + first-seen-order path
    /// the fetcher uses to opportunistically pull WinUI/WebView2.
    /// </summary>
    /// <returns>The resulting package id list.</returns>
    [Benchmark]
    public List<string> AdditionalPackagesForMixedSet() => KnownFrameworkPackageMap.AdditionalNuGetPackagesFor(_mixedRefNames);

    /// <summary>
    /// Full ref-pack scan over a synthetic 4-pack × 3-version × 2-TFM
    /// tree. Called per TFM-bucket during discovery, so its cost
    /// matters for builds that walk many TFMs.
    /// </summary>
    /// <returns>The discovered ref-pack ref dirs.</returns>
    [Benchmark]
    public List<string> RefPackProbeFullScan() => RefPackProbe.ProbeRefPackRefDirs(_packRoots, CompatibleTfms);

    /// <summary>
    /// EnumerateInstallRoots driven by a pre-captured snapshot --
    /// measures the discovery loop without paying the env-read cost
    /// in the hot path.
    /// </summary>
    /// <returns>The discovered install roots.</returns>
    [Benchmark]
    public IReadOnlyList<string> EnumerateInstallRootsFromSnapshot() => DotNetSdkLocator.EnumerateInstallRoots(_locatorInputs);

    /// <summary>
    /// Snapshot-capture cost itself -- env reads + folder paths.
    /// Lower bound on the parameterless overload's first-call cost
    /// (the lazy cache makes subsequent calls free). Returns a hash
    /// rather than the snapshot itself so the benchmark method
    /// signature stays public.
    /// </summary>
    /// <returns>A reduction of the captured snapshot for BDN to consume.</returns>
    [Benchmark]
    public int SnapshotProcessEnv() => DotNetSdkLocatorInputs.Snapshot().GetHashCode();
}
