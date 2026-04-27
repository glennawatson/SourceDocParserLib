// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.LibCompilation;
using SourceDocParser.Merge;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.TestHelpers;
using SourceDocParser.Walk;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins the pure helpers wired into the metadata pipeline:
/// <see cref="MetadataSourceLinkHelper.CollectSourceLinks"/>,
/// <see cref="MetadataWalkerHelper.BuildAssemblyWorkItems"/>, and
/// <see cref="MetadataIoHelper.PrepareOutputDirectory"/>. The async /
/// Roslyn-coupled helpers are exercised via
/// <see cref="MetadataExtractorTests"/>; the cases here keep the
/// deterministic seams isolated.
/// </summary>
public class MetadataPipelineHelperTests
{
    /// <summary>An empty type array yields no entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollectSourceLinksReturnsEmptyForEmptyInput() =>
        await Assert.That(MetadataSourceLinkHelper.CollectSourceLinks([]).Length).IsEqualTo(0);

    /// <summary>Type-level URLs are surfaced in declaration order.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollectSourceLinksCapturesTypeLevelUrls()
    {
        ApiType[] types =
        [
            TestData.ObjectType("T:A", ApiObjectKind.Class, "Asm", "https://example/a.cs"),
            TestData.ObjectType("T:B", ApiObjectKind.Class, "Asm", null),
            TestData.ObjectType("T:C", ApiObjectKind.Class, "Asm", "https://example/c.cs"),
        ];

        var entries = MetadataSourceLinkHelper.CollectSourceLinks(types);

        await Assert.That(entries.Length).IsEqualTo(2);
        await Assert.That(entries[0].Uid).IsEqualTo("T:A");
        await Assert.That(entries[0].Url).IsEqualTo("https://example/a.cs");
        await Assert.That(entries[1].Uid).IsEqualTo("T:C");
        await Assert.That(entries[1].Url).IsEqualTo("https://example/c.cs");
    }

    /// <summary>Member URLs on object types are surfaced after the parent type entry.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollectSourceLinksCapturesMemberLevelUrls()
    {
        var withMember = TestData.ObjectType("T:A", ApiObjectKind.Class, "Asm", "https://example/a.cs") with
        {
            Members =
            [
                BuildMember("M:A.Foo", "https://example/a.cs#L10"),
                BuildMember("M:A.Bar", null),
                BuildMember("M:A.Baz", "https://example/a.cs#L20"),
            ],
        };

        var entries = MetadataSourceLinkHelper.CollectSourceLinks([withMember]);

        await Assert.That(entries.Length).IsEqualTo(3);
        await Assert.That(entries[0].Uid).IsEqualTo("T:A");
        await Assert.That(entries[1].Uid).IsEqualTo("M:A.Foo");
        await Assert.That(entries[1].Url).IsEqualTo("https://example/a.cs#L10");
        await Assert.That(entries[2].Uid).IsEqualTo("M:A.Baz");
    }

    /// <summary>Empty-string URLs are treated as missing and skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CollectSourceLinksIgnoresEmptyUrls()
    {
        ApiType[] types = [TestData.ObjectType("T:A", ApiObjectKind.Class, "Asm", string.Empty)];
        await Assert.That(MetadataSourceLinkHelper.CollectSourceLinks(types).Length).IsEqualTo(0);
    }

    /// <summary>One work item is produced per (TFM, assembly) pair.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildAssemblyWorkItemsFlattensGroupsAndPaths()
    {
        var ctx = BuildContext();
        var groups = new List<TfmGroup>
        {
            new(new("net9.0", ["/a/A.dll", "/a/B.dll"], []), new RecordingLoader(), totalWalks: 2),
            new(new("net10.0", ["/b/C.dll"], []), new RecordingLoader(), totalWalks: 1),
        };

        var items = MetadataWalkerHelper.BuildAssemblyWorkItems(groups, ctx);

        await Assert.That(items.Count).IsEqualTo(3);
        await Assert.That(items[0].AssemblyPath).IsEqualTo("/a/A.dll");
        await Assert.That(items[0].Owner).IsSameReferenceAs(groups[0]);
        await Assert.That(items[1].AssemblyPath).IsEqualTo("/a/B.dll");
        await Assert.That(items[1].Owner).IsSameReferenceAs(groups[0]);
        await Assert.That(items[2].AssemblyPath).IsEqualTo("/b/C.dll");
        await Assert.That(items[2].Owner).IsSameReferenceAs(groups[1]);
        await Assert.That(items[0].Context).IsSameReferenceAs(ctx);
        await Assert.That(items[2].Context).IsSameReferenceAs(ctx);
    }

    /// <summary>An empty group list flattens to an empty work-item list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildAssemblyWorkItemsHandlesEmptyGroupList()
    {
        var items = MetadataWalkerHelper.BuildAssemblyWorkItems([], BuildContext());
        await Assert.That(items.Count).IsEqualTo(0);
    }

    /// <summary>PrepareOutputDirectory creates a missing directory.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PrepareOutputDirectoryCreatesMissingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdp-prep-{Guid.NewGuid():N}");
        try
        {
            MetadataIoHelper.PrepareOutputDirectory(path);
            await Assert.That(Directory.Exists(path)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    /// <summary>PrepareOutputDirectory wipes any existing contents before recreating the directory.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PrepareOutputDirectoryClearsExistingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdp-prep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var staleFile = Path.Combine(path, "stale.txt");
        await File.WriteAllTextAsync(staleFile, "leftover");

        try
        {
            MetadataIoHelper.PrepareOutputDirectory(path);

            await Assert.That(Directory.Exists(path)).IsTrue();
            await Assert.That(File.Exists(staleFile)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    /// <summary>Builds a minimal <see cref="ApiMember"/> with the supplied UID and source URL.</summary>
    /// <param name="uid">Member UID and display name.</param>
    /// <param name="sourceUrl">Source link URL, or null.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember BuildMember(string uid, string? sourceUrl) =>
        new(
            Name: uid,
            Uid: uid,
            Kind: ApiMemberKind.Method,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: $"void {uid}()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "T:A",
            ContainingTypeName: "A",
            SourceUrl: sourceUrl,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);

    /// <summary>Builds a <see cref="WalkContext"/> backed by no-op collaborators.</summary>
    /// <returns>A context with null walker, null resolver, and zeroed counters.</returns>
    private static WalkContext BuildContext() =>
        new(
            SymbolWalker: new NullSymbolWalker(),
            SourceLinkResolverFactory: static _ => new NullSourceLinkResolver(),
            Logger: NullLogger.Instance,
            Merger: new StreamingTypeMerger(),
            CatalogCount: new StrongBox<int>(0),
            LoadFailures: new StrongBox<int>(0));

    /// <summary>Symbol walker that always returns an empty catalog.</summary>
    private sealed class NullSymbolWalker : ISymbolWalker
    {
        /// <inheritdoc />
        public ApiCatalog Walk(string tfm, IAssemblySymbol assembly, Compilation compilation, ISourceLinkResolver sourceLinks) =>
            new(tfm, []);
    }

    /// <summary>Source-link resolver that always returns null.</summary>
    private sealed class NullSourceLinkResolver : ISourceLinkResolver
    {
        /// <inheritdoc />
        public string? Resolve(ISymbol symbol) => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    /// <summary>Compilation loader that returns a synthetic empty compilation on every load.</summary>
    private sealed class RecordingLoader : ICompilationLoader
    {
        /// <inheritdoc />
        public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(string assemblyPath, Dictionary<string, string> fallbackReferences) =>
            Load(assemblyPath, fallbackReferences, includePrivateMembers: false);

        /// <inheritdoc />
        public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(string assemblyPath, Dictionary<string, string> fallbackReferences, bool includePrivateMembers)
        {
            var compilation = CSharpCompilation.Create("Probe");
            return (compilation, compilation.Assembly);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
