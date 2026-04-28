// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.Merge;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.TestHelpers;
using SourceDocParser.Walk;

namespace SourceDocParser.Tests;

/// <summary>
/// Baseline pins for the cross-TFM merge contract. These lock down
/// the observable shape produced when the same UID surfaces under
/// multiple TFM-specific compilations: which variant wins as the
/// canonical record, how <see cref="ApiType.AppliesTo"/> aggregates,
/// how <c>inheritdoc</c> resolves within a single compilation, and
/// what happens to types or members that only exist in a non-canonical
/// TFM. Any optimisation that reduces the per-TFM Roslyn compilation
/// count must keep these contracts intact.
/// </summary>
public class CrossTfmMergeBaselineTests
{
    /// <summary>
    /// Type present in two TFMs surfaces once with both TFMs in
    /// <see cref="ApiType.AppliesTo"/> -- highest rank first.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypeOnTwoTfmsIsMergedAndAppliesToListsBoth()
    {
        var net8 = TestData.Catalog("net8.0", TestData.Type("Foo"));
        var net10 = TestData.Catalog("net10.0", TestData.Type("Foo"));

        var merged = TypeMerger.Merge([net8, net10]);

        await Assert.That(merged.Length).IsEqualTo(1);
        await Assert.That(merged[0].AppliesTo[0]).IsEqualTo("net10.0");
        await Assert.That(merged[0].AppliesTo).Contains("net8.0");
    }

    /// <summary>
    /// A type that only exists on a non-canonical (lower-ranked) TFM
    /// still appears in the merged catalog. This is the case that any
    /// "walk one TFM only" optimisation has to preserve via a
    /// metadata-only probe of the other TFMs.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypePresentOnlyOnNonCanonicalTfmIsStillSurfaced()
    {
        var net8 = TestData.Catalog("net8.0", TestData.Type("RemovedInTen"));
        var net10 = TestData.Catalog("net10.0", TestData.Type("StillThere"));

        var merged = TypeMerger.Merge([net8, net10]);

        await Assert.That(merged.Length).IsEqualTo(2);
        var removed = Array.Find(merged, t => t.FullName == "RemovedInTen");
        await Assert.That(removed).IsNotNull();
        await Assert.That(removed!.AppliesTo).IsEquivalentTo((string[])["net8.0"]);
    }

    /// <summary>
    /// When the same UID appears in multiple TFMs, the highest-ranked
    /// variant wins as the canonical record -- including its
    /// <see cref="ApiObjectType.Members"/> list. Members declared only
    /// on a lower-ranked TFM do not get merged in.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CanonicalVariantContributesMembersWholesale()
    {
        var net8Foo = TestData.ObjectType("Foo") with { Members = [Method("LegacyOnly")] };
        var net10Foo = TestData.ObjectType("Foo") with { Members = [Method("Modern")] };

        var merged = TypeMerger.Merge([
            TestData.Catalog("net8.0", net8Foo),
            TestData.Catalog("net10.0", net10Foo),
        ]);

        var foo = (ApiObjectType)GetOnlyType(merged);
        await Assert.That(foo.Members.Length).IsEqualTo(1);
        await Assert.That(foo.Members[0].Name).IsEqualTo("Modern");
    }

    /// <summary>
    /// Per-TFM documentation divergence: when both TFMs surface the
    /// same UID with different docs, the highest-ranked TFM's
    /// documentation wins.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CanonicalVariantContributesDocumentation()
    {
        var net8Foo = TestData.ObjectType("Foo") with
        {
            Documentation = new("net8 summary", string.Empty, string.Empty, string.Empty, [], [], [], [], [], null),
        };
        var net10Foo = TestData.ObjectType("Foo") with
        {
            Documentation = new("net10 summary", string.Empty, string.Empty, string.Empty, [], [], [], [], [], null),
        };

        var merged = TypeMerger.Merge([
            TestData.Catalog("net8.0", net8Foo),
            TestData.Catalog("net10.0", net10Foo),
        ]);

        await Assert.That(GetOnlyType(merged).Documentation.Summary).IsEqualTo("net10 summary");
    }

    /// <summary>
    /// SourceUrl falls through from any non-null variant when the
    /// canonical variant doesn't carry one. Pins the asymmetric merge
    /// for SourceUrl: members come from canonical only, but SourceUrl
    /// fills gaps from any lower-ranked TFM.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SourceUrlFillsFromAnyVariantWhenCanonicalLacksOne()
    {
        var net10 = TestData.Catalog("net10.0", TestData.Type("Foo", "Test", null));
        var net8 = TestData.Catalog("net8.0", TestData.Type("Foo", "Test", "https://example.test/Foo.cs#L1"));

        var merged = TypeMerger.Merge([net10, net8]);

        await Assert.That(GetOnlyType(merged).SourceUrl).IsEqualTo("https://example.test/Foo.cs#L1");
    }

    /// <summary>
    /// Inheritdoc resolution stays scoped to the compilation that
    /// produced the symbol -- the resolver doesn't reach across into
    /// other TFMs' compilations. Two TFM-specific walks therefore
    /// surface two TFM-specific resolved docs; the merger then picks
    /// the canonical one. This is the contract the
    /// metadata-probe-only-the-canonical-TFM optimisation has to keep
    /// honest: the canonical's inheritdoc must still resolve, and we
    /// accept the loss of any non-canonical-only inheritdoc base
    /// (which is rare since base types must be present on every TFM
    /// the derived class compiles for).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritDocResolutionStaysWithinSingleCompilation()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                public abstract class BaseType
                {
                    /// <summary>Base summary.</summary>
                    public abstract void Run();
                }
                public class Derived : BaseType
                {
                    /// <inheritdoc/>
                    public override void Run() { }
                }
            }
            """);

        var walker = new SymbolWalker();
        using ISourceLinkResolver resolver = new NullSourceLinkResolver();
        var catalog = walker.Walk("net10.0", compilation.Assembly, compilation, resolver);

        var derived = GetObjectType(catalog.Types, "Foo.Derived");
        var run = GetMember(derived.Members, "Run");
        await Assert.That(run.Documentation.Summary).Contains("Base summary.");
    }

    /// <summary>
    /// AppliesTo on a type seen in three TFMs aggregates all three;
    /// the order is by descending TFM rank so consumers reading
    /// <c>AppliesTo[0]</c> always see the canonical TFM.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AppliesToAggregatesAcrossThreeTfmsInRankOrder()
    {
        var merged = TypeMerger.Merge([
            TestData.Catalog("netstandard2.0", TestData.Type("Foo")),
            TestData.Catalog("net10.0", TestData.Type("Foo")),
            TestData.Catalog("net8.0", TestData.Type("Foo")),
        ]);

        var foo = GetOnlyType(merged);
        await Assert.That(foo.AppliesTo.Length).IsEqualTo(3);
        await Assert.That(foo.AppliesTo[0]).IsEqualTo("net10.0");
        await Assert.That(Contains(foo.AppliesTo, "net8.0")).IsTrue();
        await Assert.That(Contains(foo.AppliesTo, "netstandard2.0")).IsTrue();
    }

    /// <summary>
    /// The walker accepts any TFM string as a label; it does not
    /// validate or normalise the value. Pins the contract that the
    /// upstream <see cref="IAssemblySource"/> picks the TFM and the
    /// walker passes it through verbatim onto every produced
    /// <see cref="ApiType.AppliesTo"/> entry. If the source is
    /// changed to broadcast a group of TFMs onto a single walk, the
    /// AppliesTo list becomes wider but the per-TFM string contract
    /// stays the same.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalkerStampsAppliesToFromSuppliedTfm()
    {
        var compilation = BuildCompilation("namespace Foo { public class Bar { } }");
        var walker = new SymbolWalker();
        using ISourceLinkResolver resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0-android", compilation.Assembly, compilation, resolver);

        var bar = GetObjectType(catalog.Types, "Foo.Bar");
        await Assert.That(bar.AppliesTo).IsEquivalentTo((string[])["net10.0-android"]);
    }

    /// <summary>Constructs a minimal void method member for the canonical-pick test.</summary>
    /// <param name="name">Method name; also seeds Uid and Signature.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember Method(string name) => new(
        Name: name,
        Uid: $"Foo.{name}",
        Kind: ApiMemberKind.Method,
        IsStatic: false,
        IsExtension: false,
        IsRequired: false,
        IsVirtual: false,
        IsOverride: false,
        IsAbstract: false,
        IsSealed: false,
        Signature: $"void Foo.{name}()",
        Parameters: [],
        TypeParameters: [],
        ReturnType: null,
        ContainingTypeUid: "Foo",
        ContainingTypeName: "Foo",
        SourceUrl: null,
        Documentation: ApiDocumentation.Empty,
        IsObsolete: false,
        ObsoleteMessage: null,
        Attributes: []);

    /// <summary>Builds a Roslyn compilation with XML doc parsing turned on so the walker can resolve summary / inheritdoc elements.</summary>
    /// <param name="source">C# source text.</param>
    /// <returns>The compiled (but not emitted) compilation.</returns>
    private static CSharpCompilation BuildCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new(documentationMode: DocumentationMode.Parse));
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var references = new List<MetadataReference>(assemblies.Length);
        for (var i = 0; i < assemblies.Length; i++)
        {
            if (assemblies[i] is { IsDynamic: false, Location: [_, ..] location })
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
        }

        return CSharpCompilation.Create(
            "Probe",
            [tree],
            references,
            new(OutputKind.DynamicallyLinkedLibrary, xmlReferenceResolver: XmlFileResolver.Default));
    }

    /// <summary>Returns the only type in <paramref name="types"/>, or throws if the baseline shape changed.</summary>
    /// <param name="types">Merged type array.</param>
    /// <returns>The single merged type.</returns>
    private static ApiType GetOnlyType(ApiType[] types) =>
        types is [var only]
            ? only
            : throw new InvalidOperationException($"Expected exactly one merged type but found {types.Length}.");

    /// <summary>Finds the object type with the supplied full name.</summary>
    /// <param name="types">Catalog types to scan.</param>
    /// <param name="fullName">Fully qualified type name.</param>
    /// <returns>The matching object type.</returns>
    private static ApiObjectType GetObjectType(ApiType[] types, string fullName)
    {
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is ApiObjectType obj && obj.FullName == fullName)
            {
                return obj;
            }
        }

        throw new InvalidOperationException($"ApiObjectType '{fullName}' not in catalog.");
    }

    /// <summary>Finds the member with the supplied name.</summary>
    /// <param name="members">Members to scan.</param>
    /// <param name="name">Member name to find.</param>
    /// <returns>The matching member.</returns>
    private static ApiMember GetMember(ApiMember[] members, string name)
    {
        for (var i = 0; i < members.Length; i++)
        {
            if (members[i].Name == name)
            {
                return members[i];
            }
        }

        throw new InvalidOperationException($"Member '{name}' not found.");
    }

    /// <summary>Returns true when <paramref name="values"/> contains <paramref name="candidate"/>.</summary>
    /// <param name="values">Values to scan.</param>
    /// <param name="candidate">Value to match.</param>
    /// <returns>True when the value is present.</returns>
    private static bool Contains(string[] values, string candidate)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] == candidate)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>SourceLink resolver that always returns null -- used because these tests don't care about source URLs.</summary>
    private sealed class NullSourceLinkResolver : ISourceLinkResolver
    {
        /// <inheritdoc />
        public string? Resolve(ISymbol symbol) => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
