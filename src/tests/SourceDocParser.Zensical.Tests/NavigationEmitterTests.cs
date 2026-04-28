// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Navigation;
using SourceDocParser.Zensical.Options;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins the public surface of <see cref="NavigationGraphBuilder"/> --
/// package grouping under routing rules, package-name fallback,
/// alphabetic ordering, and the <c>(global)</c> bucket for namespace-
/// less types. Serialisation is the consumer's responsibility, so
/// these tests only assert against the typed graph.
/// </summary>
public class NavigationEmitterTests
{
    /// <summary>Routed types are grouped under the matching package folder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildGroupsByRoutedPackage()
    {
        var options = new ZensicalEmitterOptions([
            new(FolderName: "ReactiveUI", AssemblyPrefix: "ReactiveUI"),
        ]);
        var builder = new NavigationGraphBuilder(options);
        var typeA = TestData.ObjectType("Foo", assemblyName: "ReactiveUI") with { Namespace = "ReactiveUI" };

        var graph = builder.Build([typeA]);

        await Assert.That(graph.Packages.Length).IsEqualTo(1);
        await Assert.That(graph.Packages[0].Name).IsEqualTo("ReactiveUI");
        await Assert.That(graph.Packages[0].Namespaces[0].Name).IsEqualTo("ReactiveUI");
        await Assert.That(graph.Packages[0].Namespaces[0].Types[0].Title).IsEqualTo("Foo");
        await Assert.That(graph.Packages[0].Namespaces[0].Types[0].Path).IsEqualTo("ReactiveUI/ReactiveUI/Foo.md");
    }

    /// <summary>Without explicit rules the assembly name is the package folder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildDefaultsPackageToAssemblyName()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Foo", assemblyName: "Splat") with { Namespace = "Bar" };

        var graph = builder.Build([type]);

        await Assert.That(graph.Packages[0].Name).IsEqualTo("Splat");
        await Assert.That(graph.Packages[0].Namespaces[0].Name).IsEqualTo("Bar");
        await Assert.That(graph.Packages[0].Namespaces[0].Types[0].Path).IsEqualTo("Splat/Bar/Foo.md");
    }

    /// <summary>Types within a namespace are sorted ordinally so output is deterministic.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EntriesAreOrderedAlphabetically()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var zType = TestData.ObjectType("Zeta") with { Namespace = "Bar" };
        var aType = TestData.ObjectType("Alpha") with { Namespace = "Bar" };

        var graph = builder.Build([zType, aType]);
        var entries = graph.Packages[0].Namespaces[0].Types;

        await Assert.That(entries.Length).IsEqualTo(2);
        await Assert.That(entries[0].Title).IsEqualTo("Alpha");
        await Assert.That(entries[1].Title).IsEqualTo("Zeta");
    }

    /// <summary>Packages and namespaces are also sorted ordinally.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PackagesAndNamespacesAreOrderedAlphabetically()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var beta = TestData.ObjectType("X", assemblyName: "Beta") with { Namespace = "Z.Sub" };
        var alpha = TestData.ObjectType("Y", assemblyName: "Alpha") with { Namespace = "A.Sub" };
        var alphaSecondNs = TestData.ObjectType("W", assemblyName: "Alpha") with { Namespace = "B.Sub" };

        var graph = builder.Build([beta, alpha, alphaSecondNs]);

        await Assert.That(graph.Packages[0].Name).IsEqualTo("Alpha");
        await Assert.That(graph.Packages[1].Name).IsEqualTo("Beta");
        await Assert.That(graph.Packages[0].Namespaces[0].Name).IsEqualTo("A.Sub");
        await Assert.That(graph.Packages[0].Namespaces[1].Name).IsEqualTo("B.Sub");
    }

    /// <summary>Types without a namespace are bucketed under <c>(global)</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypesWithoutNamespaceFallToGlobalBucket()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Foo", assemblyName: "Splat") with { Namespace = string.Empty };

        var graph = builder.Build([type]);

        await Assert.That(graph.Packages[0].Namespaces[0].Name).IsEqualTo("(global)");
    }

    /// <summary>Generic-arity type names use the path-safe curly-brace form for the entry title.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GenericTypeTitleUsesAngleBracketDisplayName()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Result") with { Namespace = "App", Arity = 1 };

        var graph = builder.Build([type]);

        await Assert.That(graph.Packages[0].Namespaces[0].Types[0].Title).IsEqualTo("Result<T>");
    }

    /// <summary>Empty input produces an empty graph (no allocations beyond the empty array).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmptyInputProducesEmptyGraph()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);

        var graph = builder.Build([]);

        await Assert.That(graph.Packages.Length).IsEqualTo(0);
    }

    /// <summary>Build rejects null inputs with the standard guard.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildRejectsNullTypes()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);

        await Assert.That(() => builder.Build(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// Object-kind types round-trip onto the matching <see cref="NavigationTypeKind"/>.
    /// Drives the generic-arity-dedup story: consumers append the
    /// kind suffix to disambiguate same-named entries in the sidebar.
    /// </summary>
    /// <param name="objectKind">The source <see cref="ApiObjectKind"/>.</param>
    /// <param name="expectedNavKind">The expected nav-graph kind.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments(ApiObjectKind.Class, NavigationTypeKind.Class)]
    [Arguments(ApiObjectKind.Struct, NavigationTypeKind.Struct)]
    [Arguments(ApiObjectKind.Interface, NavigationTypeKind.Interface)]
    [Arguments(ApiObjectKind.Record, NavigationTypeKind.Record)]
    [Arguments(ApiObjectKind.RecordStruct, NavigationTypeKind.RecordStruct)]
    public async Task BuildClassifiesObjectKinds(ApiObjectKind objectKind, NavigationTypeKind expectedNavKind)
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Foo", objectKind) with { Namespace = "Demo" };

        var graph = builder.Build([type]);

        await Assert.That(graph.Packages[0].Namespaces[0].Types[0].Kind).IsEqualTo(expectedNavKind);
    }

    /// <summary>Enum types are classified as <see cref="NavigationTypeKind.Enum"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildClassifiesEnumKind()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var type = TestData.EnumType("Colour") with { Namespace = "Demo" };

        var graph = builder.Build([type]);

        await Assert.That(graph.Packages[0].Namespaces[0].Types[0].Kind).IsEqualTo(NavigationTypeKind.Enum);
    }

    /// <summary>Delegate types are classified as <see cref="NavigationTypeKind.Delegate"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildClassifiesDelegateKind()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var type = TestData.DelegateType("Action") with { Namespace = "Demo" };

        var graph = builder.Build([type]);

        await Assert.That(graph.Packages[0].Namespaces[0].Types[0].Kind).IsEqualTo(NavigationTypeKind.Delegate);
    }

    /// <summary>
    /// Generic-arity siblings keep distinct titles AND the same kind --
    /// the consumer's "{Title} {Kind}" recipe (e.g. "Change&lt;T&gt; Class")
    /// produces the visual-dedup result without further help.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildPropagatesKindAcrossArityDuplicates()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var change1 = TestData.ObjectType("Change", ApiObjectKind.Class) with { Namespace = "Demo", Arity = 1 };
        var change2 = TestData.ObjectType("Change", ApiObjectKind.Class) with { Namespace = "Demo", Arity = 2 };

        var graph = builder.Build([change1, change2]);
        var entries = graph.Packages[0].Namespaces[0].Types;

        await Assert.That(entries.Length).IsEqualTo(2);
        await Assert.That(entries[0].Title).IsEqualTo("Change<T1, T2>");
        await Assert.That(entries[0].Kind).IsEqualTo(NavigationTypeKind.Class);
        await Assert.That(entries[0].Arity).IsEqualTo(2);
        await Assert.That(entries[1].Title).IsEqualTo("Change<T>");
        await Assert.That(entries[1].Kind).IsEqualTo(NavigationTypeKind.Class);
        await Assert.That(entries[1].Arity).IsEqualTo(1);
    }

    /// <summary>
    /// A non-generic type carries <c>Arity = 0</c> and an empty
    /// <see cref="NavigationEntry.TypeParameters"/> -- the consumer
    /// can branch on that to skip generic-rendering.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildExposesZeroArityForNonGenericTypes()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Foo", ApiObjectKind.Class) with { Namespace = "Demo" };

        var graph = builder.Build([type]);
        var entry = graph.Packages[0].Namespaces[0].Types[0];

        await Assert.That(entry.Arity).IsEqualTo(0);
        await Assert.That(entry.TypeParameters.Length).IsEqualTo(0);
    }

    /// <summary>
    /// Author-given type parameter names from <see cref="ApiType.TypeParameters"/>
    /// surface verbatim on the nav entry so consumers can render
    /// <c>"Dictionary&lt;TKey, TValue&gt;"</c>-style names instead of
    /// the placeholder-only title.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildPropagatesAuthorGivenTypeParameterNames()
    {
        var builder = new NavigationGraphBuilder(ZensicalEmitterOptions.Default);
        var type = TestData.ObjectType("Dictionary", ApiObjectKind.Class) with
        {
            Namespace = "System.Collections.Generic",
            Arity = 2,
            TypeParameters = ["TKey", "TValue"],
        };

        var graph = builder.Build([type]);
        var entry = graph.Packages[0].Namespaces[0].Types[0];

        await Assert.That(entry.Arity).IsEqualTo(2);
        await Assert.That(entry.TypeParameters.Length).IsEqualTo(2);
        await Assert.That(entry.TypeParameters[0]).IsEqualTo("TKey");
        await Assert.That(entry.TypeParameters[1]).IsEqualTo("TValue");
    }
}
