// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SourceDocParser.Tfm;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Micro-benchmarks for <see cref="TfmResolver"/> -- the per-package
/// hot path called once per (lib TFM, refs/ TFM set) tuple by the
/// NuGet source. Cheap enough to run under the default
/// <c>[SimpleJob]</c>.
/// </summary>
[SimpleJob(RuntimeMoniker.Net10_0)]
[SimpleJob(RuntimeMoniker.Net11_0)]
[MemoryDiagnoser]
public class TfmResolverBenchmarks
{
    /// <summary>Net10.0 TFM.</summary>
    private const string Net100 = "net10.0";

    /// <summary>Net9.0 TFM.</summary>
    private const string Net90 = "net9.0";

    /// <summary>Net8.0 TFM.</summary>
    private const string Net80 = "net8.0";

    /// <summary>Net462 TFM.</summary>
    private const string Net462 = "net462";

    /// <summary>Net471 TFM.</summary>
    private const string Net471 = "net471";

    /// <summary>Net472 TFM.</summary>
    private const string Net472 = "net472";

    /// <summary>Net48 TFM.</summary>
    private const string Net48 = "net48";

    /// <summary>Net481 TFM.</summary>
    private const string Net481 = "net481";

    /// <summary>Netstandard2.0 TFM.</summary>
    private const string NetStandard20 = "netstandard2.0";

    /// <summary>Net10.0-android36.0 TFM.</summary>
    private const string Net100Android = "net10.0-android36.0";

    /// <summary>Refs/ TFM set covering the modern .NET majors we ship.</summary>
    private static readonly List<string> ModernRefs = [Net80, Net90, Net100];

    /// <summary>Refs/ TFM set covering the .NET Framework majors we ship.</summary>
    private static readonly List<string> FrameworkRefs = [Net462, Net471, Net472, Net48, Net481];

    /// <summary>Mixed refs/ TFM set covering modern + framework majors.</summary>
    private static readonly List<string> MixedRefs = [Net462, Net48, Net481, Net80, Net90, Net100];

    /// <summary>Exact-match path: lib/ net10.0 with a refs/ list that contains net10.0.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Benchmark]
    public string? FindBestRefsTfmExactMatch() => TfmResolver.FindBestRefsTfm(Net100, ModernRefs);

    /// <summary>Platform-suffix path: lib/ net10.0-android36.0 falls back to net10.0 in refs/.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Benchmark]
    public string? FindBestRefsTfmPlatformSuffix() => TfmResolver.FindBestRefsTfm(Net100Android, ModernRefs);

    /// <summary>Netstandard fallback path: lib/ netstandard2.0 picks the highest modern .NET refs.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Benchmark]
    public string? FindBestRefsTfmNetstandardFallback() => TfmResolver.FindBestRefsTfm(NetStandard20, ModernRefs);

    /// <summary>.NET Framework path: lib/ net48 picks net48 from a Framework-only refs set.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Benchmark]
    public string? FindBestRefsTfmFrameworkRefs() => TfmResolver.FindBestRefsTfm(Net48, FrameworkRefs);

    /// <summary>Mixed-pack path: lib/ net10.0 against a refs/ set containing both modern and Framework majors.</summary>
    /// <returns>The resolved refs/ TFM.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Benchmark]
    public string? FindBestRefsTfmMixedRefs() => TfmResolver.FindBestRefsTfm(Net100, MixedRefs);
}
