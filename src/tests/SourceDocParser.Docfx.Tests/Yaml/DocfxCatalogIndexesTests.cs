// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <see cref="DocfxCatalogIndexes"/> on the contract every
/// caller relies on: empty-singleton return on misses (no per-query
/// allocation), correct grouping for derived classes / extension
/// methods / inherited members, and the docfx-shaped emit downstream
/// when the indexes are threaded into <see cref="DocfxYamlEmitter"/>.
/// </summary>
public class DocfxCatalogIndexesTests
{
    /// <summary>Empty input returns the shared <see cref="DocfxCatalogIndexes.Empty"/> singleton.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildEmptyReturnsSingleton()
    {
        var indexes = DocfxCatalogIndexes.Build([]);

        await Assert.That(indexes).IsSameReferenceAs(DocfxCatalogIndexes.Empty);
    }

    /// <summary>Misses return the shared empty array — no per-query allocation.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnknownUidReturnsEmptyArray()
    {
        var indexes = DocfxCatalogIndexes.Empty;

        await Assert.That(indexes.GetDerived("T:Missing")).IsEmpty();
        await Assert.That(indexes.GetExtensions("T:Missing")).IsEmpty();
        await Assert.That(indexes.GetInherited("T:Missing")).IsEmpty();
    }

    /// <summary>Derived index buckets each subclass under its base type uid.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DerivedIndexBucketsByBaseUid()
    {
        var baseType = TestData.ObjectType("Base");
        var sub = TestData.ObjectType("Derived") with
        {
            BaseType = new ApiTypeReference("Base", "Base"),
        };

        var indexes = DocfxCatalogIndexes.Build([baseType, sub]);
        var derived = indexes.GetDerived("Base");

        await Assert.That(derived.Length).IsEqualTo(1);
        await Assert.That(derived[0].Uid).IsEqualTo("Derived");
    }

    /// <summary>Extension-method index buckets each method under the extended type's uid.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtensionIndexBucketsByExtendedTypeUid()
    {
        var extMethod = NewMember(
            "DoIt",
            "M:Helpers.DoIt",
            isExtension: true,
            new ApiParameter("self", new ApiTypeReference("Foo", "Foo"), false, false, false, false, false, null));
        var helpers = TestData.ObjectType("Helpers", assemblyName: "Test") with
        {
            IsStatic = true,
            Members = [extMethod],
        };

        var indexes = DocfxCatalogIndexes.Build([helpers, TestData.ObjectType("Foo")]);
        var extensions = indexes.GetExtensions("Foo");

        await Assert.That(extensions.Length).IsEqualTo(1);
        await Assert.That(extensions[0].Uid).IsEqualTo("M:Helpers.DoIt");
    }

    /// <summary>Inherited members for a class always include the System.Object baseline.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritedMembersIncludeObjectBaseline()
    {
        var indexes = DocfxCatalogIndexes.Build([TestData.ObjectType("Foo")]);
        var inherited = indexes.GetInherited("Foo");

        await Assert.That(inherited).Contains("System.Object.GetType");
        await Assert.That(inherited).Contains("System.Object.ToString");
    }

    /// <summary>Inherited members fold in the immediate base's own members when that base is in the catalog.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InheritedMembersFoldInWalkedBase()
    {
        var baseMember = NewMember("BaseRun", "M:Base.BaseRun");
        var baseType = TestData.ObjectType("Base") with { Members = [baseMember] };
        var sub = TestData.ObjectType("Derived") with
        {
            BaseType = new ApiTypeReference("Base", "Base"),
        };

        var indexes = DocfxCatalogIndexes.Build([baseType, sub]);
        var inherited = indexes.GetInherited("Derived");

        await Assert.That(inherited).Contains("M:Base.BaseRun");
    }

    /// <summary>End-to-end: page render emits the docfx blocks when the indexes carry entries.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypePageEmitsBlocksWhenIndexesPopulated()
    {
        var baseType = TestData.ObjectType("Base");
        var sub = TestData.ObjectType("Derived") with
        {
            BaseType = new ApiTypeReference("Base", "Base"),
        };
        var indexes = DocfxCatalogIndexes.Build([baseType, sub]);

        var yaml = DocfxYamlEmitter.Render(baseType, BuildInternalUids(baseType, sub), indexes);

        await Assert.That(yaml).Contains("derivedClasses:");
        await Assert.That(yaml).Contains("- Derived");
        await Assert.That(yaml).Contains("inheritedMembers:");
    }

    /// <summary>Builds a minimal HashSet of internal uids for the supplied types.</summary>
    /// <param name="types">Types to register.</param>
    /// <returns>The classifier set.</returns>
    private static HashSet<string> BuildInternalUids(params ApiType[] types)
    {
        var set = new HashSet<string>(types.Length, StringComparer.Ordinal);
        for (var i = 0; i < types.Length; i++)
        {
            set.Add(types[i].Uid);
        }

        return set;
    }

    /// <summary>Builds a minimal method-kind member with the supplied flags.</summary>
    /// <param name="name">Member name.</param>
    /// <param name="uid">Member documentation comment id.</param>
    /// <param name="isExtension">Whether the member is an extension method.</param>
    /// <param name="parameters">Optional parameter list.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember NewMember(string name, string uid, bool isExtension = false, params ApiParameter[] parameters) => new(
        Name: name,
        Uid: uid,
        Kind: ApiMemberKind.Method,
        IsStatic: isExtension,
        IsExtension: isExtension,
        IsRequired: false,
        IsVirtual: false,
        IsOverride: false,
        IsAbstract: false,
        IsSealed: false,
        Signature: name,
        Parameters: parameters,
        TypeParameters: [],
        ReturnType: null,
        ContainingTypeUid: "T:Helpers",
        ContainingTypeName: "Helpers",
        SourceUrl: null,
        Documentation: ApiDocumentation.Empty,
        IsObsolete: false,
        ObsoleteMessage: null,
        Attributes: []);
}
