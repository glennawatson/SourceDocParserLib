// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Pages;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins <see cref="PackageNamespaceTreeBuilder.Build{TEntry}"/> — the
/// shared package → namespace → entry tree builder that backs both
/// the landing-page generator and the navigation emitter. Covers the
/// routing-skip path, multi-package / multi-namespace bucketing, the
/// global-namespace fallback, ordinal sort, and the per-namespace
/// entry comparator.
/// </summary>
public class PackageNamespaceTreeBuilderTests
{
    /// <summary>Routing with one rule, used by the single-package cases.</summary>
    private static readonly PackageRoutingRule[] _singleRouting = [new("routed", "RoutedAsm")];

    /// <summary>Routing with two rules used by the multi-package case.</summary>
    private static readonly PackageRoutingRule[] _multiRouting =
    [
        new("alpha", "AlphaAsm"),
        new("beta", "BetaAsm"),
    ];

    /// <summary>Routing pointing every <c>Asm</c> assembly at the <c>pkg</c> folder.</summary>
    private static readonly PackageRoutingRule[] _pkgRouting = [new("pkg", "Asm")];

    /// <summary>Empty routing (no rule matches), used by the rejection cases.</summary>
    private static readonly PackageRoutingRule[] _noRouting = [];

    /// <summary>Types whose assembly has no matching routing rule are skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildSkipsTypesWithoutRoutingMatch()
    {
        ApiType[] types =
        [
            TestData.ObjectType("T:Routed.Foo", ApiObjectKind.Class, "RoutedAsm"),
            TestData.ObjectType("T:Unrouted.Bar", ApiObjectKind.Class, "OtherAsm"),
        ];

        var tree = PackageNamespaceTreeBuilder.Build(
            types,
            _singleRouting,
            static type => type.Uid,
            static (a, b) => string.CompareOrdinal(a, b));

        await Assert.That(tree.Count).IsEqualTo(1);
        await Assert.That(tree.ContainsKey("routed")).IsTrue();
    }

    /// <summary>Types from different assemblies / namespaces bucket independently and stay sorted.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildBucketsByPackageAndNamespace()
    {
        ApiType[] types =
        [
            TestData.ObjectType("T:NsA.B", ApiObjectKind.Class, "AlphaAsm") with { Namespace = "NsA" },
            TestData.ObjectType("T:NsA.A", ApiObjectKind.Class, "AlphaAsm") with { Namespace = "NsA" },
            TestData.ObjectType("T:NsB.X", ApiObjectKind.Class, "AlphaAsm") with { Namespace = "NsB" },
            TestData.ObjectType("T:NsZ.Y", ApiObjectKind.Class, "BetaAsm") with { Namespace = "NsZ" },
        ];

        var tree = PackageNamespaceTreeBuilder.Build(
            types,
            _multiRouting,
            static type => type.Uid,
            static (a, b) => string.CompareOrdinal(a, b));

        await Assert.That(tree.Keys).IsEquivalentTo((string[])["alpha", "beta"]);
        await Assert.That(tree["alpha"].Keys).IsEquivalentTo((string[])["NsA", "NsB"]);
        await Assert.That(tree["alpha"]["NsA"]).IsEquivalentTo((string[])["T:NsA.A", "T:NsA.B"]);
        await Assert.That(tree["alpha"]["NsB"]).IsEquivalentTo((string[])["T:NsB.X"]);
        await Assert.That(tree["beta"]["NsZ"]).IsEquivalentTo((string[])["T:NsZ.Y"]);
    }

    /// <summary>Types in the global namespace bucket under <c>(global)</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildFallsBackToGlobalNamespaceBucket()
    {
        ApiType[] types = [TestData.ObjectType("T:Global", ApiObjectKind.Class, "Asm") with { Namespace = string.Empty }];

        var tree = PackageNamespaceTreeBuilder.Build(
            types,
            _pkgRouting,
            static type => type.Uid,
            static (a, b) => string.CompareOrdinal(a, b));

        await Assert.That(tree["pkg"].Keys).IsEquivalentTo((string[])["(global)"]);
    }

    /// <summary>An empty type array yields an empty tree.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildHandlesEmptyTypeArray()
    {
        var tree = PackageNamespaceTreeBuilder.Build<string>(
            [],
            _noRouting,
            static t => t.Uid,
            static (a, b) => string.CompareOrdinal(a, b));

        await Assert.That(tree.Count).IsEqualTo(0);
    }

    /// <summary>The supplied comparator drives per-namespace sort order.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildHonoursCustomEntryComparator()
    {
        ApiType[] types =
        [
            TestData.ObjectType("T:N.AAA", ApiObjectKind.Class, "Asm") with { Namespace = "N" },
            TestData.ObjectType("T:N.B", ApiObjectKind.Class, "Asm") with { Namespace = "N" },
            TestData.ObjectType("T:N.CC", ApiObjectKind.Class, "Asm") with { Namespace = "N" },
        ];

        // Sort by descending UID length — exercises the comparator hook.
        var tree = PackageNamespaceTreeBuilder.Build(
            types,
            _pkgRouting,
            static type => type.Uid,
            static (a, b) => b.Length.CompareTo(a.Length));

        await Assert.That(tree["pkg"]["N"]).IsEquivalentTo((string[])["T:N.AAA", "T:N.CC", "T:N.B"]);
    }

    /// <summary>Null arguments are rejected up front.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildRejectsNullArguments()
    {
        ApiType[] empty = [];

        await Assert.That(() => PackageNamespaceTreeBuilder.Build<string>(null!, _noRouting, static t => t.Uid, static (_, _) => 0))
            .Throws<ArgumentNullException>();
        await Assert.That(() => PackageNamespaceTreeBuilder.Build<string>(empty, null!, static t => t.Uid, static (_, _) => 0))
            .Throws<ArgumentNullException>();
        await Assert.That(() => PackageNamespaceTreeBuilder.Build<string>(empty, _noRouting, null!, static (_, _) => 0))
            .Throws<ArgumentNullException>();
        await Assert.That(() => PackageNamespaceTreeBuilder.Build<string>(empty, _noRouting, static t => t.Uid, null!))
            .Throws<ArgumentNullException>();
    }
}
