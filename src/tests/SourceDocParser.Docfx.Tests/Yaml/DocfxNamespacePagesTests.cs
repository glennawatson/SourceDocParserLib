// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using YamlDotNet.RepresentationModel;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins the namespace-page emission contract — bucketing, sort
/// order, page rendering — against the DocfxNamespacePages helper
/// in isolation. End-to-end coverage of the items+children shape
/// happens via YamlDotNet round-trip on the rendered string.
/// </summary>
public class DocfxNamespacePagesTests
{
    /// <summary>
    /// Buckets group types by their declared namespace, sort UIDs
    /// ordinally inside each bucket, and sort buckets by namespace.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildNamespacePagesGroupsAndSortsTypes()
    {
        ApiType[] types =
        [
            TestData.ObjectType("My.Pkg.Beta") with { Uid = "T:My.Pkg.Beta", Namespace = "My.Pkg" },
            TestData.ObjectType("My.Pkg.Alpha") with { Uid = "T:My.Pkg.Alpha", Namespace = "My.Pkg" },
            TestData.ObjectType("Other.Z") with { Uid = "T:Other.Z", Namespace = "Other" },
        ];

        var pages = DocfxNamespacePages.BuildNamespacePages(types);

        await Assert.That(pages.Length).IsEqualTo(2);
        await Assert.That(pages[0].Namespace).IsEqualTo("My.Pkg");
        await Assert.That(pages[0].ChildUids).IsEquivalentTo((string[])["My.Pkg.Alpha", "My.Pkg.Beta"]);
        await Assert.That(pages[1].Namespace).IsEqualTo("Other");
        await Assert.That(pages[1].ChildUids).IsEquivalentTo((string[])["Other.Z"]);
    }

    /// <summary>
    /// Types in the global namespace (empty <c>Namespace</c> field)
    /// don't get a namespace page — docfx skips those too.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildNamespacePagesSkipsGlobalNamespace()
    {
        ApiType[] types =
        [
            TestData.ObjectType("Bare") with { Uid = "T:Bare", Namespace = string.Empty },
            TestData.ObjectType("Real.Foo") with { Uid = "T:Real.Foo", Namespace = "Real" },
        ];

        var pages = DocfxNamespacePages.BuildNamespacePages(types);

        await Assert.That(pages.Length).IsEqualTo(1);
        await Assert.That(pages[0].Namespace).IsEqualTo("Real");
    }

    /// <summary>
    /// An empty input produces an empty result rather than throwing.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildNamespacePagesReturnsEmptyForNoTypes()
    {
        var pages = DocfxNamespacePages.BuildNamespacePages([]);
        await Assert.That(pages.Length).IsEqualTo(0);
    }

    /// <summary>
    /// PathFor sanitises the namespace to a docfx-safe filename, so
    /// it lands where docfx's own emitter would put it.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PathForUsesNamespaceStemWithExtension() =>
        await Assert.That(DocfxNamespacePages.PathFor("DynamicData.Aggregation"))
            .IsEqualTo("DynamicData.Aggregation.yml");

    /// <summary>
    /// Render produces a parseable YAML page with the docfx
    /// namespace shape — uid + N: commentId + children list +
    /// type: Namespace + assemblies.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RenderEmitsDocfxNamespaceItemShape()
    {
        var page = new DocfxNamespacePages.NamespacePage(
            Namespace: "DynamicData.Aggregation",
            ChildUids: ["DynamicData.Aggregation.AggregateType", "DynamicData.Aggregation.AggregationEx"],
            AssemblyName: "DynamicData");

        var yaml = DocfxNamespacePages.Render(page);

        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        var items = (YamlSequenceNode)root[new YamlScalarNode("items")];
        await Assert.That(items.Children).Count().IsEqualTo(1);
        var item = (YamlMappingNode)items.Children[0];

        await Assert.That(item[new YamlScalarNode("uid")].ToString()).IsEqualTo("DynamicData.Aggregation");
        await Assert.That(item[new YamlScalarNode("commentId")].ToString()).IsEqualTo("N:DynamicData.Aggregation");
        await Assert.That(item[new YamlScalarNode("type")].ToString()).IsEqualTo("Namespace");

        var children = (YamlSequenceNode)item[new YamlScalarNode("children")];
        await Assert.That(children.Children).Count().IsEqualTo(2);
        await Assert.That(children.Children[0].ToString()).IsEqualTo("DynamicData.Aggregation.AggregateType");

        var assemblies = (YamlSequenceNode)item[new YamlScalarNode("assemblies")];
        await Assert.That(assemblies.Children.Single().ToString()).IsEqualTo("DynamicData");
    }

    /// <summary>
    /// Parent and child namespaces each get their own page — a type
    /// in <c>DynamicData</c> doesn't pollute <c>DynamicData.Aggregation</c>'s
    /// children list and vice versa.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ParentAndChildNamespacesProduceSeparatePages()
    {
        ApiType[] types =
        [
            TestData.ObjectType("DynamicData.RootType") with { Uid = "T:DynamicData.RootType", Namespace = "DynamicData" },
            TestData.ObjectType("DynamicData.Aggregation.NestedType") with { Uid = "T:DynamicData.Aggregation.NestedType", Namespace = "DynamicData.Aggregation" },
        ];

        var pages = DocfxNamespacePages.BuildNamespacePages(types);

        await Assert.That(pages.Length).IsEqualTo(2);

        var dynamicData = Array.Find(pages, p => p.Namespace == "DynamicData");
        var aggregation = Array.Find(pages, p => p.Namespace == "DynamicData.Aggregation");

        await Assert.That(dynamicData.ChildUids).IsEquivalentTo((string[])["DynamicData.RootType"]);
        await Assert.That(aggregation.ChildUids).IsEquivalentTo((string[])["DynamicData.Aggregation.NestedType"]);
    }
}
