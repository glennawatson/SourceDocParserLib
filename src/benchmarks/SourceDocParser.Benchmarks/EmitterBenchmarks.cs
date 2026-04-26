// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using SourceDocParser.Docfx;
using SourceDocParser.Zensical;

namespace SourceDocParser.Benchmarks;

/// <summary>
/// Side-by-side render benchmarks for the two emitters that ship with
/// the parser — <see cref="ZensicalDocumentationEmitter"/> (mkdocs
/// Material Markdown) and <see cref="DocfxYamlEmitter"/> (docfx
/// ManagedReference YAML). Both walk the exact same canonical type
/// set so the per-type cost is directly comparable.
/// </summary>
/// <remarks>
/// The benchmark measures the in-memory render path
/// (<c>TypePageEmitter.Render</c> / <c>DocfxYamlEmitter.Render</c>)
/// rather than the disk-write path so iteration cost is dominated by
/// markup formatting, not I/O. A full-pipeline benchmark that does hit
/// disk lives in <see cref="MetadataExtractorBenchmarks"/>.
/// </remarks>
[ShortRunJob]
[MemoryDiagnoser]
public class EmitterBenchmarks
{
    /// <summary>Pre-built canonical type set used by every benchmark method in this run.</summary>
    private List<ApiType> _types = null!;

    /// <summary>
    /// Gets or sets the synthesised type count per iteration. Picked to
    /// bracket a typical project (~100 types), the slim fixture (~600),
    /// and a heavy real-world owner-driven walk (~2000).
    /// </summary>
    [Params(100, 600, 2000)]
    public int TypeCount { get; set; }

    /// <summary>
    /// Gets or sets the synthesised member count per type. Tracks how
    /// the emitter scales with the per-type member surface — small (5,
    /// e.g. a record), wide (30, e.g. a WPF control).
    /// </summary>
    [Params(5, 30)]
    public int MembersPerType { get; set; }

    /// <summary>
    /// Materialises the canonical type set once per parameter
    /// combination so iterations don't rebuild fixtures.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _types = new(TypeCount);
        for (var i = 0; i < TypeCount; i++)
        {
            _types.Add(BuildType($"Type{i:D5}", MembersPerType));
        }
    }

    /// <summary>Renders every type via the Zensical Markdown emitter.</summary>
    /// <returns>Total characters across the rendered pages.</returns>
    [Benchmark(Baseline = true)]
    public long ZensicalMarkdown()
    {
        long total = 0;
        for (var i = 0; i < _types.Count; i++)
        {
            total += TypePageEmitter.Render(_types[i]).Length;
        }

        return total;
    }

    /// <summary>Renders every type via the docfx YAML emitter.</summary>
    /// <returns>Total characters across the rendered pages.</returns>
    [Benchmark]
    public long DocfxYaml()
    {
        long total = 0;
        for (var i = 0; i < _types.Count; i++)
        {
            total += DocfxYamlEmitter.Render(_types[i]).Length;
        }

        return total;
    }

    /// <summary>
    /// Builds a synthetic <see cref="ApiObjectType"/> with
    /// <paramref name="memberCount"/> distinct method members. Methods
    /// are picked because they exercise the longest output path on
    /// both emitters (signature + parameters + return).
    /// </summary>
    /// <param name="uid">Type UID (also used as Name + FullName).</param>
    /// <param name="memberCount">Number of synthesised members.</param>
    /// <returns>The constructed type.</returns>
    private static ApiObjectType BuildType(string uid, int memberCount)
    {
        var members = new List<ApiMember>(memberCount);
        for (var i = 0; i < memberCount; i++)
        {
            var name = $"Method{i:D2}";
            members.Add(new(
                Name: name,
                Uid: $"M:{uid}.{name}",
                Kind: ApiMemberKind.Method,
                IsStatic: false,
                IsExtension: false,
                IsRequired: false,
                IsVirtual: false,
                IsOverride: false,
                IsAbstract: false,
                IsSealed: false,
                Signature: $"public string {name}(int arg, string text)",
                Parameters:
                [
                    new("arg", new("Int32", "T:System.Int32"), false, false, false, false, false, null),
                    new("text", new("String", "T:System.String"), false, false, false, false, false, null),
                ],
                TypeParameters: [],
                ReturnType: new("String", "T:System.String"),
                ContainingTypeUid: uid,
                ContainingTypeName: uid,
                SourceUrl: null,
                Documentation: ApiDocumentation.Empty,
                IsObsolete: false,
                ObsoleteMessage: null,
                Attributes: []));
        }

        return new(
            Name: uid,
            FullName: uid,
            Uid: uid,
            Namespace: "Bench",
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
            Members: [.. members]);
    }
}
