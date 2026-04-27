// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins <see cref="ZensicalMemberDisplayName"/> on the heading shapes
/// the markdown emitter needs: <c>TypeName(args)</c> for ctors,
/// <c>Type.Method(args)</c> for methods, and the bare
/// <c>Type.Property</c> form for non-paren kinds.
/// </summary>
public class ZensicalMemberDisplayNameTests
{
    /// <summary>Constructor heading drops the <c>.ctor</c> noise and renders as <c>TypeName(args)</c>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorHeadingUsesTypeName()
    {
        var ctor = NewMember(".ctor", ApiMemberKind.Constructor);

        var heading = ZensicalMemberDisplayName.Heading(ctor, TestData.ObjectType("ReactiveObject"));

        await Assert.That(heading).IsEqualTo("ReactiveObject()");
    }

    /// <summary>Method heading qualifies with the type name and lists parameter type displays.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MethodHeadingQualifiesAndLowersParameters()
    {
        var method = NewMember(
            "Run",
            ApiMemberKind.Method,
            new ApiParameter("arg", new ApiTypeReference("int", "T:System.Int32"), false, false, false, false, false, null));

        var heading = ZensicalMemberDisplayName.Heading(method, TestData.ObjectType("Foo"));

        await Assert.That(heading).IsEqualTo("Foo.Run(int)");
    }

    /// <summary>Property heading is <c>Type.Property</c> with no parens.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PropertyHeadingHasNoParens()
    {
        var prop = NewMember("HasFlags", ApiMemberKind.Property);

        var heading = ZensicalMemberDisplayName.Heading(prop, TestData.ObjectType("Foo"));

        await Assert.That(heading).IsEqualTo("Foo.HasFlags");
    }

    /// <summary>Builds a minimal member with the requested name + kind + optional parameters.</summary>
    /// <param name="name">Member metadata name.</param>
    /// <param name="kind">Member kind.</param>
    /// <param name="parameters">Optional parameter list.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember NewMember(string name, ApiMemberKind kind, params ApiParameter[] parameters) => new(
        Name: name,
        Uid: $"X:Foo.{name}",
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
