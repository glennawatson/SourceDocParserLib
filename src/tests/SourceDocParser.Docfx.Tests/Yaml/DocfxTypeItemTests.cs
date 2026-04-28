// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins individual fields on the docfx type item -- anything emitted
/// directly by <see cref="DocfxYamlBuilderExtensions.AppendTypeItem(System.Text.StringBuilder,SourceDocParser.Model.ApiType)"/>
/// that doesn't have its own dedicated test fixture lands here.
/// </summary>
public class DocfxTypeItemTests
{
    /// <summary>Type item carries a <c>parent: namespace</c> field for namespaced types.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypeItemEmitsParentNamespace()
    {
        var type = TestData.ObjectType("Foo") with { Namespace = "My.Lib" };

        var yaml = DocfxYamlEmitter.Render(type);

        await Assert.That(yaml).Contains("  parent: My.Lib");
    }

    /// <summary>Global-namespace types skip the type-item <c>parent:</c> field entirely (reference entries may still carry their own).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypeItemSkipsParentForGlobalNamespace()
    {
        var type = TestData.ObjectType("Foo");

        var yaml = DocfxYamlEmitter.Render(type);

        // Type item lays out as `id: Foo` then directly `langs:` --
        // any inserted `parent:` would land between them. Reference
        // entries render their own `parent:` deeper in the file but
        // those don't affect the type item's own header.
        await Assert.That(yaml).Contains("id: Foo\n  langs:");
    }

    /// <summary>Constructor item id quotes <c>'#ctor'</c> and emits the friendly name + overload anchor.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorItemQuotesIdAndEmitsOverload()
    {
        var ctor = new ApiMember(
            Name: ".ctor",
            Uid: "M:Foo.#ctor",
            Kind: ApiMemberKind.Constructor,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "Foo()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "T:Foo",
            ContainingTypeName: "Foo",
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);
        var type = TestData.ObjectType("Foo") with { Members = [ctor] };

        var yaml = DocfxYamlEmitter.Render(type);

        // Quoter picks double quotes; either '#ctor' or "#ctor" is valid
        // YAML -- assert the value survived as a quoted scalar rather than
        // pinning a specific style.
        await Assert.That(yaml).Contains("id: \"#ctor\"").Or.Contains("id: '#ctor'");
        await Assert.That(yaml).Contains("name: Foo()");
        await Assert.That(yaml).Contains("overload: Foo.#ctor*");
    }

    /// <summary>Children list is alphabetised by uid (docfx convention) regardless of declaration order.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ChildrenListIsAlphabeticalByUid()
    {
        ApiMember[] members =
        [
            NewSimpleMethod("Zeta", "M:Foo.Zeta"),
            NewSimpleMethod("Alpha", "M:Foo.Alpha"),
            NewSimpleMethod("Mid", "M:Foo.Mid"),
        ];
        var type = TestData.ObjectType("Foo") with { Members = members };

        var yaml = DocfxYamlEmitter.Render(type);
        var alphaIdx = yaml.IndexOf("\n  - Foo.Alpha\n", StringComparison.Ordinal);
        var midIdx = yaml.IndexOf("\n  - Foo.Mid\n", StringComparison.Ordinal);
        var zetaIdx = yaml.IndexOf("\n  - Foo.Zeta\n", StringComparison.Ordinal);

        await Assert.That(alphaIdx).IsGreaterThan(-1);
        await Assert.That(midIdx).IsGreaterThan(alphaIdx);
        await Assert.That(zetaIdx).IsGreaterThan(midIdx);
    }

    /// <summary>Member item field sequence matches docfx -- overload sits AFTER the syntax block, not above langs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MemberItemFieldsAreInDocfxOrder()
    {
        var ctor = new ApiMember(
            Name: ".ctor",
            Uid: "M:Foo.#ctor",
            Kind: ApiMemberKind.Constructor,
            IsStatic: false,
            IsExtension: false,
            IsRequired: false,
            IsVirtual: false,
            IsOverride: false,
            IsAbstract: false,
            IsSealed: false,
            Signature: "Foo()",
            Parameters: [],
            TypeParameters: [],
            ReturnType: null,
            ContainingTypeUid: "T:Foo",
            ContainingTypeName: "Foo",
            SourceUrl: null,
            Documentation: ApiDocumentation.Empty,
            IsObsolete: false,
            ObsoleteMessage: null,
            Attributes: []);
        var type = TestData.ObjectType("Foo") with { Members = [ctor] };

        var yaml = DocfxYamlEmitter.Render(type);
        var syntaxIdx = yaml.IndexOf("\n  syntax:", StringComparison.Ordinal);
        var overloadIdx = yaml.IndexOf("\n  overload:", StringComparison.Ordinal);
        var langsIdx = yaml.IndexOf("\n  langs:", StringComparison.Ordinal);

        await Assert.That(syntaxIdx).IsGreaterThan(-1);
        await Assert.That(overloadIdx).IsGreaterThan(syntaxIdx);
        await Assert.That(overloadIdx).IsGreaterThan(langsIdx);
    }

    /// <summary>Builds a minimal void-method member with the given name + uid.</summary>
    /// <param name="name">Method name.</param>
    /// <param name="uid">Method uid.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember NewSimpleMethod(string name, string uid) => new(
        Name: name,
        Uid: uid,
        Kind: ApiMemberKind.Method,
        IsStatic: false,
        IsExtension: false,
        IsRequired: false,
        IsVirtual: false,
        IsOverride: false,
        IsAbstract: false,
        IsSealed: false,
        Signature: $"void {name}()",
        Parameters: [],
        TypeParameters: [],
        ReturnType: null,
        ContainingTypeUid: "T:Foo",
        ContainingTypeName: "Foo",
        SourceUrl: null,
        Documentation: ApiDocumentation.Empty,
        IsObsolete: false,
        ObsoleteMessage: null,
        Attributes: []);
}
