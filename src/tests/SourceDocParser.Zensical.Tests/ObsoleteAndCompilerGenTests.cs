// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.TestHelpers;
using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Pins the Zensical-layer obsolete and compiler-generated handling:
/// the deprecated admonition + frontmatter tag fire on
/// <c>IsObsolete</c>, and angle-bracketed metadata names are skipped
/// at the emitter so display-class artefacts never get pages.
/// </summary>
public class ObsoleteAndCompilerGenTests
{
    /// <summary>Obsolete types render the danger-style deprecation admonition under the heading.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ObsoleteTypeRendersDeprecationAdmonition()
    {
        var type = TestData.ObjectType("Legacy") with
        {
            IsObsolete = true,
            ObsoleteMessage = "Use NewThing instead.",
        };

        var page = TypePageEmitter.Render(type);

        await Assert.That(page).Contains("!!! danger \"Deprecated\"");
        await Assert.That(page).Contains("Use NewThing instead.");
    }

    /// <summary>Obsolete types include the <c>obsolete</c> tag in the YAML frontmatter.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ObsoleteTypeAddsObsoleteFrontmatterTag()
    {
        var type = TestData.ObjectType("Legacy") with { IsObsolete = true };

        var page = TypePageEmitter.Render(type);

        await Assert.That(page).Contains("  - obsolete");
    }

    /// <summary>Compiler-generated types (angle-bracketed names) don't produce pages.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task CompilerGeneratedTypesAreSkipped()
    {
        using var scratch = new TempDirectory();
        var legitimate = TestData.ObjectType("Foo");
        var displayClass = TestData.ObjectType("<>c__DisplayClass0_0");

        var pages = await new ZensicalDocumentationEmitter().EmitAsync([legitimate, displayClass], scratch.Path);

        // Only Foo + 1 package landing + 1 namespace landing -- the
        // display class never reaches the page emitter.
        await Assert.That(pages).IsEqualTo(3);
    }

    /// <summary>Type-level Attributes render under the heading, with compiler markers filtered.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypeAttributesRenderUnderHeading()
    {
        var type = TestData.ObjectType("Foo") with
        {
            Attributes =
            [
                new("Serializable", "T:System.SerializableAttribute", string.Empty, []),
                new("NullableContext", "T:System.Runtime.CompilerServices.NullableContextAttribute", string.Empty, []),
            ],
        };

        var page = TypePageEmitter.Render(type);

        await Assert.That(page).Contains("**Attributes:** `[Serializable]`");
        await Assert.That(page).DoesNotContain("NullableContext");
    }

    /// <summary>
    /// Records loaded from metadata expose a synthesised <c>&lt;Clone&gt;$</c>
    /// method whose <c>IsImplicitlyDeclared</c> bit is lost. The type
    /// page must not link to a member file for it -- otherwise docfx
    /// emits "target not found" warnings because the emitter skips
    /// writing those member pages.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TypePageOmitsCompilerGeneratedMethodsFromMembersTable()
    {
        ApiMember[] members =
        [
            BuildMethod("Clone", "Foo Clone()"),
            BuildMethod("<Clone>$", "Foo <Clone>$()"),
        ];
        var type = TestData.ObjectType("Foo") with { Members = members };

        var page = TypePageEmitter.Render(type);

        await Assert.That(page).Contains("](Foo/Clone.md)");
        await Assert.That(page).DoesNotContain("{Clone}$");
        await Assert.That(page).DoesNotContain("<Clone>");
    }

    /// <summary>
    /// The <see cref="ZensicalDocumentationEmitter"/> output must not
    /// link a record's <c>&lt;Clone&gt;$</c> from the type page since
    /// no member file is written for it.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EmitDoesNotProduceCloneDollarLinkInTypePage()
    {
        using var scratch = new TempDirectory();
        ApiMember[] members =
        [
            BuildMethod("Clone", "Foo Clone()"),
            BuildMethod("<Clone>$", "Foo <Clone>$()"),
        ];
        var type = TestData.ObjectType("Foo") with { Members = members, Namespace = "Demo" };

        await new ZensicalDocumentationEmitter().EmitAsync([type], scratch.Path);

        var typePage = await File.ReadAllTextAsync(Path.Combine(scratch.Path, "Test", "Demo", "Foo.md"));
        var clonePath = Path.Combine(scratch.Path, "Test", "Demo", "Foo", "{Clone}$.md");

        await Assert.That(typePage).DoesNotContain("{Clone}$");
        await Assert.That(File.Exists(clonePath)).IsFalse();
    }

    /// <summary>Builds a minimal method-kind <see cref="ApiMember"/>.</summary>
    /// <param name="name">Member metadata name.</param>
    /// <param name="signature">Display signature.</param>
    /// <returns>The constructed member.</returns>
    private static ApiMember BuildMethod(string name, string signature) => new(
        Name: name,
        Uid: $"M:Foo.{name}",
        Kind: ApiMemberKind.Method,
        IsStatic: false,
        IsExtension: false,
        IsRequired: false,
        IsVirtual: false,
        IsOverride: false,
        IsAbstract: false,
        IsSealed: false,
        Signature: signature,
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
}
