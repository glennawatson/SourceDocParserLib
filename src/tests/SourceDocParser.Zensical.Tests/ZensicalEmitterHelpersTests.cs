// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Zensical.Pages;

namespace SourceDocParser.Zensical.Tests;

/// <summary>Direct tests for the shared Zensical formatting helpers that the page emitters build on.</summary>
public class ZensicalEmitterHelpersTests
{
    /// <summary>Fixture value for ResultTypeName.</summary>
    private const string ResultTypeName = "Result";

    /// <summary>Display formatting for multi-arity generics renders the expected placeholder list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatDisplayTypeNameHandlesMultiDigitArity()
    {
        const int Arity = 12;
        var formatted = ZensicalEmitterHelpers.FormatDisplayTypeName(ResultTypeName, Arity);

        await Assert.That(formatted).IsEqualTo("Result<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>");
    }

    /// <summary>Path formatting uses slash-separated namespaces and curly-brace generic placeholders.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildTypePathFormatsNamespaceAndGenericType()
    {
        const int Arity = 2;
        var path = ZensicalEmitterHelpers.BuildTypePath("My.Library", ResultTypeName, Arity, ".md");

        await Assert.That(path).IsEqualTo("My/Library/Result{T1,T2}.md");
    }

    /// <summary>Type filenames cannot collide with namespace landing pages on case-insensitive filesystems.</summary>
    /// <param name="namespaceName">Namespace containing the type.</param>
    /// <param name="typeName">Type name to route.</param>
    /// <param name="arity">Number of generic parameters.</param>
    /// <param name="expected">Expected relative type-page path.</param>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    [Arguments("System", "Index", 0, "System/Index-type.md")]
    [Arguments("System", "index", 0, "System/index-type.md")]
    [Arguments("System", "INDEX", 0, "System/INDEX-type.md")]
    [Arguments("", "Index", 0, "_global/Index-type.md")]
    [Arguments("System", "Index", 1, "System/Index{T}.md")]
    [Arguments("System", "Indexer", 0, "System/Indexer.md")]
    public async Task BuildTypePathAvoidsLandingPageCollision(string namespaceName, string typeName, int arity, string expected)
    {
        var path = ZensicalEmitterHelpers.BuildTypePath(namespaceName, typeName, arity, ".md");

        await Assert.That(path).IsEqualTo(expected);
    }

    /// <summary>Member paths keep the global-namespace folder and place the member stem under the type folder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildMemberPathUsesGlobalNamespaceFolder()
    {
        var path = ZensicalEmitterHelpers.BuildMemberPath(string.Empty, ResultTypeName, 1, "Run{T}_Core_Impl", ".md");

        await Assert.That(path).IsEqualTo("_global/Result{T}/Run{T}_Core_Impl.md");
    }

    /// <summary>Member paths escape reserved leaf names while preserving other sanitized names.</summary>
    /// <param name="name">Metadata member name.</param>
    /// <param name="expected">Portable member path stem.</param>
    /// <returns>The asynchronous assertions.</returns>
    [Test]
    [Arguments("Indexer", "Indexer")]
    [Arguments("Index<T>", "Index{T}")]
    [Arguments("Nested/Index", "Nested/Index-member")]
    [Arguments("Nested\\INDEX", "Nested\\INDEX-member")]
    [Arguments("Build_/Themes/Index.axaml", "Build_/Themes/Index_axaml")]
    public async Task MemberFileStemPreservesNonReservedNames(string name, string expected) =>
        await Assert.That(ZensicalEmitterHelpers.MemberFileStem(name)).IsEqualTo(expected);

    /// <summary>Filename sanitization only rewrites the small set of path-hostile characters.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SanitiseForFilenameHandlesSafeAndUnsafeValues()
    {
        await Assert.That(ZensicalEmitterHelpers.SanitiseForFilename("Safe_Name")).IsEqualTo("Safe_Name");
        await Assert.That(ZensicalEmitterHelpers.SanitiseForFilename("Run<T>.Core:Impl")).IsEqualTo("Run{T}_Core_Impl");
    }
}
