// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="SymbolWalker"/> and <see cref="ISymbolWalker"/>
/// driven against a tiny in-memory <see cref="CSharpCompilation"/> built
/// from source -- no Roslyn-loaded DLL needed.
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
        await Assert.That(Array.Exists(catalog.Types, static t => t.FullName == "Foo.Bar")).IsTrue();
        await Assert.That(Array.Exists(catalog.Types, static t => t.FullName == "Foo.IQux")).IsTrue();
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

        await Assert.That(Array.Exists(catalog.Types, static t => t.FullName == "Foo.Hidden")).IsFalse();
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
    /// A custom <see cref="IDocResolver"/> factory passed to the walker
    /// constructor is invoked once per <see cref="SymbolWalker.Walk"/>
    /// call with the compilation whose symbols are being walked.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalkInvokesInjectedDocResolverFactoryPerCall()
    {
        var compilation = BuildCompilation("namespace Foo { public class Bar { } }");
        var factoryCalls = new List<Compilation>();
        var walker = new SymbolWalker(c =>
        {
            factoryCalls.Add(c);
            return new RecordingDocResolver();
        });
        using ISourceLinkResolver resolver = new NullSourceLinkResolver();

        walker.Walk("net10.0", compilation.Assembly, compilation, resolver);
        walker.Walk("net10.0", compilation.Assembly, compilation, resolver);

        await Assert.That(factoryCalls.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(factoryCalls[0], compilation)).IsTrue();
    }

    /// <summary>
    /// A single walker instance can serve multiple concurrent walk calls
    /// because each call builds its own scoped caches.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalkSupportsConcurrentCalls()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                public class Bar
                {
                    public int Baz() => 42;
                }
            }
            """);
        var walker = new SymbolWalker();

        var catalogs = await Task.WhenAll(
            Enumerable.Range(0, 4)
                .Select(async i =>
                {
                    await Task.Yield();
                    using ISourceLinkResolver resolver = new NullSourceLinkResolver();
                    return (Index: i, Catalog: walker.Walk("net10.0", compilation.Assembly, compilation, resolver));
                }));

        await Assert.That(catalogs.Length).IsEqualTo(4);
        await Assert.That(Array.TrueForAll(catalogs, static c => Array.Exists(c.Catalog.Types, static t => t.FullName == "Foo.Bar"))).IsTrue();
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
                .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(static a => MetadataReference.CreateFromFile(a.Location)),
        ];
        return CSharpCompilation.Create("Test", [tree], references);
    }

    /// <summary>
    /// Recording <see cref="IDocResolver"/> used to verify the walker
    /// hands resolution to the injected instance.
    /// </summary>
    private sealed class RecordingDocResolver : IDocResolver
    {
        /// <inheritdoc />
        public ApiDocumentation Resolve(ISymbol symbol) => ApiDocumentation.Empty;
    }

    /// <summary>
    /// <see cref="ISourceLinkResolver"/> implementation that always returns
    /// null -- used by walker tests that don't care about source URLs.
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
