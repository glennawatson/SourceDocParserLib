// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Tests;

/// <summary>
/// Direct tests for the shared Zensical formatting helpers that the page emitters build on.
/// </summary>
public class ZensicalEmitterHelpersTests
{
    /// <summary>
    /// Display formatting for multi-arity generics renders the expected placeholder list.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FormatDisplayTypeNameHandlesMultiDigitArity()
    {
        var formatted = ZensicalEmitterHelpers.FormatDisplayTypeName("Result", 12);

        await Assert.That(formatted).IsEqualTo("Result<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>");
    }

    /// <summary>
    /// Path formatting uses slash-separated namespaces and curly-brace generic placeholders.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildTypePathFormatsNamespaceAndGenericType()
    {
        var path = ZensicalEmitterHelpers.BuildTypePath("My.Library", "Result", 2, ".md");

        await Assert.That(path).IsEqualTo("My/Library/Result{T1,T2}.md");
    }

    /// <summary>
    /// Member paths keep the global-namespace folder and place the member stem under the type folder.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildMemberPathUsesGlobalNamespaceFolder()
    {
        var path = ZensicalEmitterHelpers.BuildMemberPath(string.Empty, "Result", 1, "Run{T}_Core_Impl", ".md");

        await Assert.That(path).IsEqualTo("_global/Result{T}/Run{T}_Core_Impl.md");
    }

    /// <summary>
    /// Filename sanitization only rewrites the small set of path-hostile characters.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SanitiseForFilenameHandlesSafeAndUnsafeValues()
    {
        await Assert.That(ZensicalEmitterHelpers.SanitiseForFilename("Safe_Name")).IsEqualTo("Safe_Name");
        await Assert.That(ZensicalEmitterHelpers.SanitiseForFilename("Run<T>.Core:Impl")).IsEqualTo("Run{T}_Core_Impl");
    }
}
