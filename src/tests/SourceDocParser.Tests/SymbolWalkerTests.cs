// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
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
    /// <summary>Fixture value for Net100.</summary>
    private const string Net100 = "net10.0";

    /// <summary>Expected fixture value used by WalkInvokesInjectedDocResolverFactoryPerCall.</summary>
    private const int WalkInvokesInjectedDocResolverFactoryPerCallExpectedValue = 2;

    /// <summary>Expected fixture value used by WalkSupportsConcurrentCalls.</summary>
    private const int WalkSupportsConcurrentCallsRange = 4;

    /// <summary>Walks a synthetic compilation and asserts the resulting catalog contains the public types we declared in source.</summary>
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

        var catalog = walker.Walk(Net100, compilation.Assembly, compilation, resolver);

        await Assert.That(catalog.Tfm).IsEqualTo(Net100);
        await Assert.That(Array.Exists(catalog.Types, static t => t.FullName == "Foo.Bar")).IsTrue();
        await Assert.That(Array.Exists(catalog.Types, static t => t.FullName == "Foo.IQux")).IsTrue();
    }

    /// <summary>Walking a compilation with no public types yields an empty type list.</summary>
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

        var catalog = walker.Walk(Net100, compilation.Assembly, compilation, resolver);

        await Assert.That(Array.Exists(catalog.Types, static t => t.FullName == "Foo.Hidden")).IsFalse();
    }

    /// <summary>Null/whitespace TFM and null collaborators throw at the entry point.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalkValidatesArguments()
    {
        var compilation = BuildCompilation("public class A { }");
        var walker = new SymbolWalker();
        using ISourceLinkResolver resolver = new NullSourceLinkResolver();

        await Assert.That(() => walker.Walk(" ", compilation.Assembly, compilation, resolver)).Throws<ArgumentException>();
        await Assert.That(() => walker.Walk(Net100, null!, compilation, resolver)).Throws<ArgumentNullException>();
        await Assert.That(() => walker.Walk(Net100, compilation.Assembly, null!, resolver)).Throws<ArgumentNullException>();
        await Assert.That(() => walker.Walk(Net100, compilation.Assembly, compilation, null!)).Throws<ArgumentNullException>();
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

        _ = walker.Walk(Net100, compilation.Assembly, compilation, resolver);
        _ = walker.Walk(Net100, compilation.Assembly, compilation, resolver);

        await Assert.That(factoryCalls.Count).IsEqualTo(WalkInvokesInjectedDocResolverFactoryPerCallExpectedValue);
        await Assert.That(ReferenceEquals(factoryCalls[0], compilation)).IsTrue();
    }

    /// <summary>A single walker instance can serve multiple concurrent walk calls because each call builds its own scoped caches.</summary>
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

        var walks = new Task<ApiCatalog>[WalkSupportsConcurrentCallsRange];
        for (var i = 0; i < walks.Length; i++)
        {
            walks[i] = WalkAsync();
        }

        var catalogs = await Task.WhenAll(walks);

        await Assert.That(catalogs.Length).IsEqualTo(WalkSupportsConcurrentCallsRange);
        await Assert.That(Array.TrueForAll(catalogs, static catalog => Array.Exists(catalog.Types, static type => type.FullName == "Foo.Bar"))).IsTrue();

        async Task<ApiCatalog> WalkAsync()
        {
            await Task.Yield();
            using ISourceLinkResolver resolver = new NullSourceLinkResolver();
            return walker.Walk(Net100, compilation.Assembly, compilation, resolver);
        }
    }

    /// <summary>Builds an in-memory <see cref="CSharpCompilation"/> from <paramref name="source"/> referencing the runtime assemblies of the currently-loaded BCL.</summary>
    /// <param name="source">C# source text to compile.</param>
    /// <returns>The compiled (but not emitted) compilation.</returns>
    private static CSharpCompilation BuildCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        List<MetadataReference> references =
        [
            .. WalkerTestFixtures.GetRuntimeReferences(),
        ];
        return CSharpCompilation.Create(nameof(Test), [tree], references);
    }

    /// <summary>Recording <see cref="IDocResolver"/> used to verify the walker hands resolution to the injected instance.</summary>
    private sealed class RecordingDocResolver : IDocResolver
    {
        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ApiDocumentation Resolve(ISymbol symbol) => ApiDocumentation.Empty;
    }

    /// <summary><see cref="ISourceLinkResolver"/> implementation that always returns null -- used by walker tests that don't care about source URLs.</summary>
    private sealed class NullSourceLinkResolver : ISourceLinkResolver
    {
        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? Resolve(ISymbol symbol) => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
