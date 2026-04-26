// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.TestHelpers;

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

        // Only Foo + 1 package landing + 1 namespace landing — the
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
                new("Serializable", "T:System.SerializableAttribute", []),
                new("NullableContext", "T:System.Runtime.CompilerServices.NullableContextAttribute", []),
            ],
        };

        var page = TypePageEmitter.Render(type);

        await Assert.That(page).Contains("**Attributes:** `[Serializable]`");
        await Assert.That(page).DoesNotContain("NullableContext");
    }
}
