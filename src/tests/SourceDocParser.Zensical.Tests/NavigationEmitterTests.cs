// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

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
}
