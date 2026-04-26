// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests;

/// <summary>
/// Drives <see cref="SymbolWalker"/> against a real
/// <see cref="CSharpCompilation"/> that uses the C# 14
/// <c>extension(T receiver) { ... }</c> block syntax. Verifies that
/// the walker (a) drops the synthesised marker types instead of
/// emitting bogus pages for them and (b) populates
/// <see cref="ApiObjectType.ExtensionBlocks"/> with the receiver
/// parameter and conceptual members declared inside each block.
/// </summary>
public class CSharp14ExtensionWalkerTests
{
    /// <summary>An extension block on a static class produces a single ExtensionBlocks entry on the parent.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtensionBlockSurfacesOnParentObjectType()
    {
        var compilation = BuildCompilation(
            """
            public static class Helpers
            {
                extension(string source)
                {
                    public bool IsEmpty => source.Length == 0;
                }
            }
            """);
        var walker = new SymbolWalker();
        var resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0", compilation.Assembly, compilation, resolver);

        await Assert.That(catalog.Types).HasSingleItem();
        var helpers = (ApiObjectType)catalog.Types[0];
        await Assert.That(helpers.Name).IsEqualTo("Helpers");
        await Assert.That(helpers.ExtensionBlocks).HasSingleItem();
        var block = helpers.ExtensionBlocks[0];
        await Assert.That(block.ReceiverName).IsEqualTo("source");
        await Assert.That(block.Receiver.DisplayName).Contains("string").Or.Contains("String");
    }

    /// <summary>The synthesised extension marker type does not surface as its own catalog entry.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task MarkerTypeDoesNotSurfaceAsApiType()
    {
        var compilation = BuildCompilation(
            """
            public static class Helpers
            {
                extension(int value)
                {
                    public int Doubled => value * 2;
                }
            }
            """);
        var walker = new SymbolWalker();
        var resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0", compilation.Assembly, compilation, resolver);

        // Only the Helpers type — no <>E__N marker page slipping through.
        await Assert.That(catalog.Types.Length).IsEqualTo(1);
        await Assert.That(catalog.Types[0].Name).IsEqualTo("Helpers");
    }

    /// <summary>Builds an in-memory compilation against the runtime's BCL references with C# preview features enabled.</summary>
    /// <param name="source">C# source text to compile.</param>
    /// <returns>The compiled (but not emitted) compilation.</returns>
    private static CSharpCompilation BuildCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        List<MetadataReference> references =
        [
            .. AppDomain.CurrentDomain.GetAssemblies()
                .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(static a => MetadataReference.CreateFromFile(a.Location)),
        ];
        return CSharpCompilation.Create(
            "CSharp14ExtTest",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>SourceLink resolver that returns null for every symbol — keeps the walker tests free of a real PDB dependency.</summary>
    private sealed class NullSourceLinkResolver : ISourceLinkResolver
    {
        /// <inheritdoc />
        public string? Resolve(ISymbol symbol) => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
