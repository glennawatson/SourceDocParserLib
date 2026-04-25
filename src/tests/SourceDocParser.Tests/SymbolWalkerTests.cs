// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="SymbolWalker"/> and <see cref="ISymbolWalker"/>
/// driven against a tiny in-memory <see cref="CSharpCompilation"/> built
/// from source — no Roslyn-loaded DLL needed.
/// </summary>
public class SymbolWalkerTests
{
    /// <summary>
    /// Walks a synthetic compilation and asserts the resulting catalog
    /// contains the public types we declared in source.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalkExtractsPublicTypesFromCompilation()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                public class Bar
                {
                    public int Baz() => 42;
                }

                public interface IQux
                {
                    void Run();
                }
            }
            """);

        var walker = new SymbolWalker();
        using ISourceLinkResolver resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0", compilation.Assembly, compilation, resolver);

        await Assert.That(catalog.Tfm).IsEqualTo("net10.0");
        await Assert.That(catalog.Types.Any(t => t.FullName == "Foo.Bar")).IsTrue();
        await Assert.That(catalog.Types.Any(t => t.FullName == "Foo.IQux")).IsTrue();
    }

    /// <summary>
    /// Walking a compilation with no public types yields an empty type list.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalkReturnsEmptyForInternalOnlyAssembly()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                internal class Hidden { }
            }
            """);

        var walker = new SymbolWalker();
        using ISourceLinkResolver resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0", compilation.Assembly, compilation, resolver);

        await Assert.That(catalog.Types.Any(t => t.FullName == "Foo.Hidden")).IsFalse();
    }

    /// <summary>
    /// Null/whitespace TFM and null collaborators throw at the entry point.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalkValidatesArguments()
    {
        var compilation = BuildCompilation("public class A { }");
        var walker = new SymbolWalker();
        using ISourceLinkResolver resolver = new NullSourceLinkResolver();

        await Assert.That(() => walker.Walk(" ", compilation.Assembly, compilation, resolver)).Throws<ArgumentException>();
        await Assert.That(() => walker.Walk("net10.0", null!, compilation, resolver)).Throws<ArgumentNullException>();
        await Assert.That(() => walker.Walk("net10.0", compilation.Assembly, null!, resolver)).Throws<ArgumentNullException>();
        await Assert.That(() => walker.Walk("net10.0", compilation.Assembly, compilation, null!)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// Builds an in-memory <see cref="CSharpCompilation"/> from <paramref name="source"/>
    /// referencing the runtime assemblies of the currently-loaded BCL.
    /// </summary>
    /// <param name="source">C# source text to compile.</param>
    /// <returns>The compiled (but not emitted) compilation.</returns>
    private static CSharpCompilation BuildCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        List<MetadataReference> references =
        [
            .. AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location)),
        ];
        return CSharpCompilation.Create("Test", [tree], references);
    }

    /// <summary>
    /// <see cref="ISourceLinkResolver"/> implementation that always returns
    /// null — used by walker tests that don't care about source URLs.
    /// </summary>
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
