// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.Walk;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Direct tests for the small per-scope cache types that back the main-library hot paths.
/// </summary>
public class ScopedCacheTests
{
    /// <summary>
    /// The doc resolver cache builds once per symbol and reuses the cached instance thereafter.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DocResolveCacheMemoisesPerSymbol()
    {
        var compilation = BuildCompilation("namespace Foo { public class Bar { } }");
        var symbol = compilation.GetTypeByMetadataName("Foo.Bar")!;
        var cache = new DocResolveCache();
        var calls = 0;

        var first = cache.GetOrAdd(
            symbol,
            (Summary: "First", Calls: new Counter(() => calls++)),
            static (candidate, state) =>
            {
                _ = candidate;
                state.Calls.Increment();
                return new(state.Summary, string.Empty, string.Empty, string.Empty, [], [], [], [], [], null);
            });
        var second = cache.GetOrAdd(
            symbol,
            (Summary: "Second", Calls: new Counter(() => calls++)),
            static (candidate, state) =>
            {
                _ = candidate;
                state.Calls.Increment();
                return new(state.Summary, string.Empty, string.Empty, string.Empty, [], [], [], [], [], null);
            });

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(first.Summary).IsEqualTo("First");
        await Assert.That(calls).IsEqualTo(1);
    }

    /// <summary>
    /// The namespace display-name cache formats once and then reuses the cached string for the same symbol.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NamespaceDisplayNameCacheMemoisesPerNamespace()
    {
        var compilation = BuildCompilation("namespace Foo.Bar { public class Baz { } }");
        var ns = compilation.GetTypeByMetadataName("Foo.Bar.Baz")!.ContainingNamespace;
        var cache = new NamespaceDisplayNameCache();

        var first = cache.GetOrAdd(ns);
        var second = cache.GetOrAdd(ns);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
        await Assert.That(first).IsEqualTo("Foo.Bar");
    }

    /// <summary>
    /// Builds an in-memory <see cref="CSharpCompilation"/> from <paramref name="source"/>.
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
        return CSharpCompilation.Create("CacheTest", [tree], references);
    }

    /// <summary>
    /// Small mutable callback wrapper so the static cache-miss lambdas can increment a local counter.
    /// </summary>
    /// <param name="callback">Action to run on increment.</param>
    private sealed class Counter(Action callback)
    {
        /// <summary>
        /// Runs the wrapped callback.
        /// </summary>
        public void Increment() => callback();
    }
}
