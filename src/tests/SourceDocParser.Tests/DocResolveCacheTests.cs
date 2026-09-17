// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.Model;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>Tests recursive resolution and failed factory recovery.</summary>
public sealed class DocResolveCacheTests
{
    /// <summary>Nested resolutions may resize the cache without losing the outer builder's result.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task GetOrAdd_NestedBuildersResizeDictionary_PreservesOuterResult()
    {
        const int NestedSymbolCount = 16;
        var symbol = CSharpCompilation.Create("OuterCacheTest").Assembly;
        var cache = new DocResolveCache();
        var expected = ApiDocumentation.Empty with { Summary = "Outer documentation." };

        var result = cache.GetOrAdd(symbol, expected, (candidate, documentation) =>
        {
            for (var i = 0; i < NestedSymbolCount; i++)
            {
                var nested = CSharpCompilation.Create($"{candidate.Name}{i}").Assembly;
                _ = cache.GetOrAdd(nested, documentation, static (_, value) => value);
            }

            return documentation;
        });
        var cached = cache.GetOrAdd(
            symbol,
            expected,
            static (_, _) => throw new InvalidOperationException("Outer result must remain cached."));

        await Assert.That(ReferenceEquals(expected, result)).IsTrue();
        await Assert.That(ReferenceEquals(expected, cached)).IsTrue();
    }

    /// <summary>A failed factory leaves the symbol available for another resolution attempt.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task GetOrAdd_FailedBuilder_RetriesAndCachesResult()
    {
        var symbol = CSharpCompilation.Create("CacheTest").Assembly;
        var cache = new DocResolveCache();
        var expected = ApiDocumentation.Empty with { Summary = "Recovered documentation." };

        await Assert.That(() => cache.GetOrAdd(
            symbol,
            expected,
            static (_, _) => throw new InvalidOperationException("Builder failed.")))
            .Throws<InvalidOperationException>();

        var result = cache.GetOrAdd(symbol, expected, static (_, documentation) => documentation);
        var cached = cache.GetOrAdd(
            symbol,
            expected,
            static (_, _) => throw new InvalidOperationException("Cached result must be reused."));

        await Assert.That(ReferenceEquals(expected, result)).IsTrue();
        await Assert.That(ReferenceEquals(expected, cached)).IsTrue();
    }

    /// <summary>A recursive request for an active symbol returns empty documentation until its factory completes.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task GetOrAdd_RecursiveBuilder_ReturnsEmptyUntilCompleted()
    {
        var symbol = CSharpCompilation.Create("CacheTest").Assembly;
        var cache = new DocResolveCache();
        var expected = ApiDocumentation.Empty with { Summary = "Completed documentation." };
        ApiDocumentation? recursive = null;

        var result = cache.GetOrAdd(symbol, expected, (candidate, documentation) =>
        {
            recursive = cache.GetOrAdd(
                candidate,
                documentation,
                static (_, _) => throw new InvalidOperationException("Active factory must not run twice."));
            return documentation;
        });

        await Assert.That(ReferenceEquals(ApiDocumentation.Empty, recursive)).IsTrue();
        await Assert.That(ReferenceEquals(expected, result)).IsTrue();
        await Assert.That(ReferenceEquals(expected, cache.GetOrAdd(symbol, expected, static (_, doc) => doc))).IsTrue();
    }
}
