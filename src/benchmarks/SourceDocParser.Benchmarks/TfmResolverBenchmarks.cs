// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using SourceDocParser.Tfm;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Micro-benchmarks for <see cref="TfmResolver"/> — the per-package
/// hot path called once per (lib TFM, refs/ TFM set) tuple by the
/// NuGet source. Cheap enough to run under the default
/// <c>[SimpleJob]</c>.
/// </summary>
[MemoryDiagnoser]
public class TfmResolverBenchmarks
{
    /// <summary>refs/ TFM set covering the modern .NET majors we ship.</summary>
    private static readonly List<string> ModernRefs = ["net8.0", "net9.0", "net10.0"];

    /// <summary>refs/ TFM set covering the .NET Framework majors we ship.</summary>
    private static readonly List<string> FrameworkRefs = ["net462", "net471", "net472", "net48", "net481"];

    /// <summary>Mixed refs/ TFM set covering modern + framework majors.</summary>
    private static readonly List<string> MixedRefs = ["net462", "net48", "net481", "net8.0", "net9.0", "net10.0"];

    /// <summary>Exact-match path: lib/ net10.0 with a refs/ list that contains net10.0.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmExactMatch() => TfmResolver.FindBestRefsTfm("net10.0", ModernRefs);

    /// <summary>Platform-suffix path: lib/ net10.0-android36.0 falls back to net10.0 in refs/.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmPlatformSuffix() => TfmResolver.FindBestRefsTfm("net10.0-android36.0", ModernRefs);

    /// <summary>netstandard fallback path: lib/ netstandard2.0 picks the highest modern .NET refs.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmNetstandardFallback() => TfmResolver.FindBestRefsTfm("netstandard2.0", ModernRefs);

    /// <summary>.NET Framework path: lib/ net48 picks net48 from a Framework-only refs set.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmFrameworkRefs() => TfmResolver.FindBestRefsTfm("net48", FrameworkRefs);

    /// <summary>Mixed-pack path: lib/ net10.0 against a refs/ set containing both modern and Framework majors.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmMixedRefs() => TfmResolver.FindBestRefsTfm("net10.0", MixedRefs);
}
