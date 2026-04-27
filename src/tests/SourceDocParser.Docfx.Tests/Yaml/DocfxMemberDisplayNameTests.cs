// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Docfx.Yaml;
using SourceDocParser.Model;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.Docfx.Tests.Yaml;

/// <summary>
/// Pins <see cref="DocfxMemberDisplayName"/>: docfx-style friendly
/// names for constructors, methods, properties; <c>(arg, arg)</c>
/// rendering with type display names; the <c>uid + "*"</c> overload
/// anchor; and the qualified / fully-qualified composition forms.
/// </summary>
public class DocfxMemberDisplayNameTests
{
    /// <summary>Constructors render with the containing type name + parens.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorRendersAsTypeNameWithParens()
    {
        var ctor = NewMember("ctor", ".ctor", ApiMemberKind.Constructor);

        var name = DocfxMemberDisplayName.Unqualified(ctor, TestData.ObjectType("ReactiveObject"));

        await Assert.That(name).IsEqualTo("ReactiveObject()");
    }

    /// <summary>Constructor with parameters lists their display names comma-separated.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorWithParametersListsTypes()
    {
        var ctor = NewMember(
            "ctor",
            ".ctor",
            ApiMemberKind.Constructor,
            new ApiParameter("first", new("int", "T:System.Int32"), false, false, false, false, false, null),
            new ApiParameter("second", new("string", "T:System.String"), false, false, false, false, false, null));

        var name = DocfxMemberDisplayName.Unqualified(ctor, TestData.ObjectType("ReactiveObject"));

        await Assert.That(name).IsEqualTo("ReactiveObject(int, string)");
    }

    /// <summary>Properties keep their bare name — no parens.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PropertyRendersAsPlainName()
    {
        var property = NewMember("HasFlags", "P:Foo.HasFlags", ApiMemberKind.Property);

        var name = DocfxMemberDisplayName.Unqualified(property, TestData.ObjectType("Foo"));

        await Assert.That(name).IsEqualTo("HasFlags");
    }

    /// <summary>Qualified name prepends the containing type's simple name.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task QualifiedPrependsTypeSimpleName()
    {
        var method = NewMember("Run", "M:Foo.Run", ApiMemberKind.Method);

        var qualified = DocfxMemberDisplayName.Qualified(method, TestData.ObjectType("Foo"));

        await Assert.That(qualified).IsEqualTo("Foo.Run()");
    }

    /// <summary>Fully-qualified name prepends the type's full name (namespace included).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FullyQualifiedPrependsTypeFullName()
    {
        var method = NewMember("Run", "M:My.Lib.Foo.Run", ApiMemberKind.Method);
        var type = TestData.ObjectType("Foo") with { Namespace = "My.Lib", FullName = "My.Lib.Foo" };

        var full = DocfxMemberDisplayName.FullyQualified(method, type);

        await Assert.That(full).IsEqualTo("My.Lib.Foo.Run()");
    }

    /// <summary>Overload anchor is the UID with a trailing <c>*</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task OverloadAnchorAppendsAsterisk()
    {
        var anchor = DocfxMemberDisplayName.OverloadAnchor("ReactiveUI.ReactiveObject.#ctor");

        await Assert.That(anchor).IsEqualTo("ReactiveUI.ReactiveObject.#ctor*");
    }

    /// <summary>Builds a minimal member of the requested kind with the supplied parameters.</summary>
    /// <param name="name">Display name (Roslyn metadata name).</param>
    /// <param name="uid">Documentation comment ID.</param>
    /// <param name="kind">Member kind.</param>
    /// <param name="parameters">Optional parameter list.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember NewMember(string name, string uid, ApiMemberKind kind, params ApiParameter[] parameters) => new(
        Name: name == "ctor" ? ".ctor" : name,
        Uid: uid,
        Kind: kind,
        IsStatic: false,
        IsExtension: false,
        IsRequired: false,
        IsVirtual: false,
        IsOverride: false,
        IsAbstract: false,
        IsSealed: false,
        Signature: name,
        Parameters: parameters,
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
