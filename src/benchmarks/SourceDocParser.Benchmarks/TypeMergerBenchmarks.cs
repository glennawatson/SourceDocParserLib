// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using SourceDocParser.Merge;
using SourceDocParser.Model;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Micro-benchmark for <see cref="TypeMerger.Merge"/> — the dedup pass
/// that collapses per-TFM catalogs into one canonical view per type
/// UID. Driven by synthetic catalogs of varying type counts so we
/// can spot N×log(N) regressions in the sort + bucket-build pipeline.
/// </summary>
[MemoryDiagnoser]
public class TypeMergerBenchmarks
{
    /// <summary>TFMs the synthetic catalogs span (mirrors the slim fixture).</summary>
    private static readonly string[] Tfms = ["net8.0", "net9.0", "net10.0"];

    /// <summary>Pre-built per-iteration catalogs used by every benchmark method.</summary>
    private List<ApiCatalog> _catalogs = null!;

    /// <summary>
    /// Gets or sets the number of distinct types (each repeated across every TFM)
    /// per benchmark run. Picked to bracket the slim fixture (~600 types) and the
    /// full owner-driven run (~2000).
    /// </summary>
    [Params(100, 600, 2000)]
    public int TypeCount { get; set; }

    /// <summary>Builds the per-TFM catalogs once for the parameter combination so iterations don't rebuild fixtures.</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _catalogs = new(Tfms.Length);
        for (var tfmIndex = 0; tfmIndex < Tfms.Length; tfmIndex++)
        {
            var tfm = Tfms[tfmIndex];
            var types = new List<ApiType>(TypeCount);
            for (var i = 0; i < TypeCount; i++)
            {
                types.Add(BuildType($"Type{i:D5}"));
            }

            _catalogs.Add(new(tfm, [.. types]));
        }
    }

    /// <summary>Measures one merge over the pre-built catalogs.</summary>
    /// <returns>The merged canonical types.</returns>
    [Benchmark]
    public ApiType[] Merge() => TypeMerger.Merge(_catalogs);

    /// <summary>
    /// Builds a minimal <see cref="ApiType"/> with the supplied UID. All
    /// other fields are empty defaults — the merger only cares about
    /// UID, TFM source, and FullName for ordering.
    /// </summary>
    /// <param name="uid">Unique identifier (also used as Name + FullName).</param>
    /// <returns>The constructed type.</returns>
    private static ApiObjectType BuildType(string uid) =>
        new(
            Name: uid,
            FullName: uid,
            Uid: uid,
            Namespace: string.Empty,
            Arity: 0,
            IsStatic: false,
            IsSealed: false,
            IsAbstract: false,
            AssemblyName: "Bench",
            Documentation: ApiDocumentation.Empty,
            BaseType: null,
            Interfaces: [],
            SourceUrl: null,
            AppliesTo: [],
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: [],
            Kind: ApiObjectKind.Class,
            IsReadOnly: false,
            IsByRefLike: false,
            Members: [],
            ExtensionBlocks: []);
}
