// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Exercises owner searches and documentation-root selection.</summary>
public class NuGetFetcherInternalsTests
{
    /// <summary>Number of eligible packages in the owner-search fixture.</summary>
    private const int EligibleOwnerCount = 2;

    /// <summary>The owner search URI carries owner, paging, and the SemVer level.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildOwnerSearchUriComposesOwnerPagingAndSemverLevel()
    {
        var endpoint = new Uri("https://search.example.org/query");

        var uri = NuGetFetcher.BuildOwnerSearchUri(endpoint, "reactiveui", take: 100, skip: 200);
        var query = uri.Query;

        await Assert.That(query.Contains("owner:reactiveui", StringComparison.Ordinal)).IsTrue();
        await Assert.That(query.Contains("take=100", StringComparison.Ordinal)).IsTrue();
        await Assert.That(query.Contains("skip=200", StringComparison.Ordinal)).IsTrue();
        await Assert.That(query.Contains("semVerLevel=2.0.0", StringComparison.Ordinal)).IsTrue();
        await Assert.That(uri.Host).IsEqualTo("search.example.org");
    }

    /// <summary>Owner names with reserved characters are URL-escaped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildOwnerSearchUriEscapesOwner()
    {
        var endpoint = new Uri("https://search.example.org/query");

        var uri = NuGetFetcher.BuildOwnerSearchUri(endpoint, "Foo Bar+Baz", take: 1, skip: 0);

        await Assert.That(uri.Query.Contains("Foo%20Bar%2BBaz", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>A clean result (no deprecation, no vulnerabilities) is not skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShouldSkipOwnerSearchResultAcceptsCleanPackage()
    {
        using var doc = JsonDocument.Parse("""{ "id": "Pkg" }""");
        await Assert.That(NuGetFetcher.ShouldSkipOwnerSearchResult(doc.RootElement)).IsFalse();
    }

    /// <summary>A deprecated package (any deprecation block) is skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShouldSkipOwnerSearchResultRejectsDeprecatedPackage()
    {
        using var doc = JsonDocument.Parse("""{ "id": "Pkg", "deprecation": { "message": "old" } }""");
        await Assert.That(NuGetFetcher.ShouldSkipOwnerSearchResult(doc.RootElement)).IsTrue();
    }

    /// <summary>A non-empty <c>vulnerabilities</c> array marks the package as skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShouldSkipOwnerSearchResultRejectsVulnerablePackage()
    {
        using var doc = JsonDocument.Parse("""{ "id": "Pkg", "vulnerabilities": [{"severity": "high"}] }""");
        await Assert.That(NuGetFetcher.ShouldSkipOwnerSearchResult(doc.RootElement)).IsTrue();
    }

    /// <summary>An empty <c>vulnerabilities</c> array does NOT mark the package as skipped.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShouldSkipOwnerSearchResultIgnoresEmptyVulnerabilitiesArray()
    {
        using var doc = JsonDocument.Parse("""{ "id": "Pkg", "vulnerabilities": [] }""");
        await Assert.That(NuGetFetcher.ShouldSkipOwnerSearchResult(doc.RootElement)).IsFalse();
    }

    /// <summary>The page mapper appends only clean package IDs.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddEligibleOwnerPackageIdsFiltersAndCollects()
    {
        using var doc = JsonDocument.Parse("""
            {
              "data": [
                { "id": "Clean.A" },
                { "id": "Deprecated.B", "deprecation": {} },
                { "id": "Vuln.C", "vulnerabilities": [{"severity":"high"}] },
                { "id": "Clean.D", "vulnerabilities": [] }
              ]
            }
            """);
        var ids = new List<string>();

        NuGetFetcher.AddEligibleOwnerPackageIds(doc.RootElement, ids);

        await Assert.That(ids.Count).IsEqualTo(EligibleOwnerCount);
        await Assert.That(ids[0]).IsEqualTo("Clean.A");
        await Assert.That(ids[1]).IsEqualTo("Clean.D");
    }
}
