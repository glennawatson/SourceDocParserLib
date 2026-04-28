// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using SourceDocParser.Tfm;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Micro-benchmarks for <see cref="TfmResolver"/> -- the per-package
/// hot path called once per (lib TFM, refs/ TFM set) tuple by the
/// NuGet source. Cheap enough to run under the default
/// <c>[SimpleJob]</c>.
/// </summary>
[MemoryDiagnoser]
public class TfmResolverBenchmarks
{
    /// <summary>net10.0 TFM.</summary>
    private const string Net100 = "net10.0";

    /// <summary>net9.0 TFM.</summary>
    private const string Net90 = "net9.0";

    /// <summary>net8.0 TFM.</summary>
    private const string Net80 = "net8.0";

    /// <summary>net462 TFM.</summary>
    private const string Net462 = "net462";

    /// <summary>net471 TFM.</summary>
    private const string Net471 = "net471";

    /// <summary>net472 TFM.</summary>
    private const string Net472 = "net472";

    /// <summary>net48 TFM.</summary>
    private const string Net48 = "net48";

    /// <summary>net481 TFM.</summary>
    private const string Net481 = "net481";

    /// <summary>netstandard2.0 TFM.</summary>
    private const string NetStandard20 = "netstandard2.0";

    /// <summary>net10.0-android36.0 TFM.</summary>
    private const string Net100Android = "net10.0-android36.0";

    /// <summary>refs/ TFM set covering the modern .NET majors we ship.</summary>
    private static readonly List<string> ModernRefs = [Net80, Net90, Net100];

    /// <summary>refs/ TFM set covering the .NET Framework majors we ship.</summary>
    private static readonly List<string> FrameworkRefs = [Net462, Net471, Net472, Net48, Net481];

    /// <summary>Mixed refs/ TFM set covering modern + framework majors.</summary>
    private static readonly List<string> MixedRefs = [Net462, Net48, Net481, Net80, Net90, Net100];

    /// <summary>Exact-match path: lib/ net10.0 with a refs/ list that contains net10.0.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmExactMatch() => TfmResolver.FindBestRefsTfm(Net100, ModernRefs);

    /// <summary>Platform-suffix path: lib/ net10.0-android36.0 falls back to net10.0 in refs/.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmPlatformSuffix() => TfmResolver.FindBestRefsTfm(Net100Android, ModernRefs);

    /// <summary>netstandard fallback path: lib/ netstandard2.0 picks the highest modern .NET refs.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmNetstandardFallback() => TfmResolver.FindBestRefsTfm(NetStandard20, ModernRefs);

    /// <summary>.NET Framework path: lib/ net48 picks net48 from a Framework-only refs set.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmFrameworkRefs() => TfmResolver.FindBestRefsTfm(Net48, FrameworkRefs);

    /// <summary>Mixed-pack path: lib/ net10.0 against a refs/ set containing both modern and Framework majors.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [Benchmark]
    public string? FindBestRefsTfmMixedRefs() => TfmResolver.FindBestRefsTfm(Net100, MixedRefs);
}
