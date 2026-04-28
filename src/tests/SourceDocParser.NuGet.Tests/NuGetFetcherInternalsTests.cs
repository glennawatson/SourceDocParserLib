// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Direct coverage of the internal helpers extracted from
/// <see cref="NuGetFetcher"/>: transitive-dependency filtering, the
/// owner search URI builder, the deprecated/vulnerable result skip
/// predicate, JSON-to-list mapping, and the new-package batch
/// builder. These run pure-in-memory so the slow integration walk
/// stays as the single end-to-end gate.
/// </summary>
public class NuGetFetcherInternalsTests
{
    /// <summary>A user-excluded ID is rejected by the transitive-dependency filter.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShouldIncludeTransitiveDependencyRejectsUserExcludedId()
    {
        var request = BuildRequest(excludeIds: ["Newtonsoft.Json"]);
        await Assert.That(NuGetFetcher.ShouldIncludeTransitiveDependency("Newtonsoft.Json", request)).IsFalse();
    }

    /// <summary>A user-excluded prefix (case-insensitive) is rejected.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShouldIncludeTransitiveDependencyRejectsUserExcludedPrefix()
    {
        var request = BuildRequest(excludePrefixes: ["System."]);
        await Assert.That(NuGetFetcher.ShouldIncludeTransitiveDependency("System.Memory", request)).IsFalse();
    }

    /// <summary>A default-skip native runtime package is rejected even without user filters.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShouldIncludeTransitiveDependencyRejectsDefaultSkipPackage()
    {
        var request = BuildRequest();
        await Assert.That(NuGetFetcher.ShouldIncludeTransitiveDependency("runtime.linux-x64.Microsoft.NETCore.App", request)).IsFalse();
    }

    /// <summary>An ordinary package ID with no exclusion match is included.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ShouldIncludeTransitiveDependencyAcceptsOrdinaryId()
    {
        var request = BuildRequest();
        await Assert.That(NuGetFetcher.ShouldIncludeTransitiveDependency("ReactiveUI", request)).IsTrue();
    }

    /// <summary>Includes that pass the filter accumulate into <see cref="HashSet{T}"/> and seed the seen-set.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AddEligibleDependencyIdsAccumulatesNewIdsAndUpdatesSeen()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Already.Seen" };
        var request = BuildRequest(seen: seen, excludeIds: ["Excluded.Pkg"]);
        var newIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        NuGetFetcher.AddEligibleDependencyIds(
            ["Already.Seen", "Excluded.Pkg", "runtime.foo", "Fresh.Pkg", "Another.Pkg"],
            request,
            newIds);

        await Assert.That(newIds.Contains("Fresh.Pkg")).IsTrue();
        await Assert.That(newIds.Contains("Another.Pkg")).IsTrue();
        await Assert.That(newIds.Count).IsEqualTo(2);
        await Assert.That(seen.Contains("Fresh.Pkg")).IsTrue();
        await Assert.That(seen.Contains("Another.Pkg")).IsTrue();
    }

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

        await Assert.That(ids.Count).IsEqualTo(2);
        await Assert.That(ids[0]).IsEqualTo("Clean.A");
        await Assert.That(ids[1]).IsEqualTo("Clean.D");
    }

    /// <summary>Validated extraction writes the entry bytes and preserves the ZIP timestamp.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractValidatedEntryWritesFileAndPreservesTimestamp()
    {
        var entryTimestamp = new DateTimeOffset(2024, 01, 02, 03, 04, 06, TimeSpan.Zero);
        await using var archiveStream = BuildArchive(("lib/net8.0/Foo.xml", "payload"), entryTimestamp);
        await using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("lib/net8.0/Foo.xml");
        var destPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            NuGetFetcher.ExtractValidatedEntry(destPath, entry!);

            await Assert.That(await File.ReadAllTextAsync(destPath, CancellationToken.None)).IsEqualTo("payload");
            await Assert.That(File.GetLastWriteTimeUtc(destPath)).IsEqualTo(entry!.LastWriteTime.UtcDateTime);
        }
        finally
        {
            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }
        }
    }

    /// <summary>Files written by validated extraction satisfy the skip-if-identical fast path.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractValidatedEntryProducesFileRecognizedByIsSameAsExtracted()
    {
        var entryTimestamp = new DateTimeOffset(2024, 01, 02, 03, 04, 06, TimeSpan.Zero);
        await using var archiveStream = BuildArchive(("lib/net8.0/Foo.dll", "abc"), entryTimestamp);
        await using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("lib/net8.0/Foo.dll");
        var destPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            NuGetFetcher.ExtractValidatedEntry(destPath, entry!);

            await Assert.That(NuGetFetcher.IsSameAsExtracted(destPath, entry!)).IsTrue();
        }
        finally
        {
            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }
        }
    }

    /// <summary>The transitive batch carries each ID with its TFM override (or null when missing).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildTransitivePackageBatchAppliesTfmOverrides()
    {
        var newIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Pkg.A", "Pkg.B" };
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Pkg.A"] = "net9.0",
        };

        var batch = NuGetFetcher.BuildTransitivePackageBatch(newIds, overrides);

        await Assert.That(batch.Length).IsEqualTo(2);
        var byId = batch.ToDictionary(static p => p.Id, StringComparer.Ordinal);
        await Assert.That(byId["Pkg.A"].Tfm).IsEqualTo("net9.0");
        await Assert.That(byId["Pkg.B"].Tfm).IsNull();
        await Assert.That(byId["Pkg.A"].Version).IsNull();
    }

    /// <summary>An empty new-id set produces an empty batch.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildTransitivePackageBatchHandlesEmptyInput()
    {
        var batch = NuGetFetcher.BuildTransitivePackageBatch([], []);
        await Assert.That(batch.Length).IsEqualTo(0);
    }

    /// <summary>
    /// Persisting the primary-id sidecar writes one id per line in the
    /// fetch order -- that's the contract the assembly source's
    /// reader (<see cref="NuGetAssemblySource.ReadPrimaryIdsSidecar"/>)
    /// is built around.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WritePrimaryPackagesSidecarPersistsIdsInOrder()
    {
        var apiPath = Path.Combine(Path.GetTempPath(), $"fetcher-sidecar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(apiPath);
        try
        {
            (string Id, string? Version, string? Tfm)[] packages =
            [
                ("ReactiveUI", null, null),
                ("Splat", "15.0.0", null),
                ("DynamicData", null, "net10.0"),
            ];

            NuGetFetcher.WritePrimaryPackagesSidecar(apiPath, packages);

            var sidecar = Path.Combine(apiPath, NuGetAssemblySource.PrimaryPackagesFileName);
            var ids = NuGetAssemblySource.ReadPrimaryIdsSidecar(sidecar);

            await Assert.That(ids).IsEquivalentTo((string[])["ReactiveUI", "Splat", "DynamicData"]);
        }
        finally
        {
            Directory.Delete(apiPath, recursive: true);
        }
    }

    /// <summary>
    /// Empty fetch (manifest with no owners and no additionalPackages)
    /// still writes the sidecar so a stale sidecar from a previous
    /// fetch can't leak primary ids back into the next discovery pass.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WritePrimaryPackagesSidecarTruncatesOnEmptyFetch()
    {
        var apiPath = Path.Combine(Path.GetTempPath(), $"fetcher-sidecar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(apiPath);
        var sidecar = Path.Combine(apiPath, NuGetAssemblySource.PrimaryPackagesFileName);
        await File.WriteAllTextAsync(sidecar, "Stale.Package\n");
        try
        {
            NuGetFetcher.WritePrimaryPackagesSidecar(apiPath, []);

            await Assert.That(File.Exists(sidecar)).IsTrue();
            await Assert.That(NuGetAssemblySource.ReadPrimaryIdsSidecar(sidecar).Length).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(apiPath, recursive: true);
        }
    }

    /// <summary>Builds a <see cref="TransitiveDependencyResolutionRequest"/> with sane defaults.</summary>
    /// <param name="seen">Pre-seeded "already-seen" identifier set, or null for an empty one.</param>
    /// <param name="excludeIds">Exact-match exclude IDs, or null for no exact excludes.</param>
    /// <param name="excludePrefixes">Prefix-match excludes, or null for no prefix excludes.</param>
    /// <returns>The constructed request.</returns>
    private static TransitiveDependencyResolutionRequest BuildRequest(
        HashSet<string>? seen = null,
        string[]? excludeIds = null,
        string[]? excludePrefixes = null) =>
        new(
            LibDir: "/lib",
            CacheDir: "/cache",
            SeenIds: seen ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ExcludeIds: excludeIds ?? [],
            ExcludePrefixes: excludePrefixes ?? [],
            TfmOverrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            TfmPreference: [],
            Logger: NullLogger.Instance,
            CancellationToken: CancellationToken.None);

    /// <summary>Builds an in-memory ZIP containing a single UTF-8 text entry with a controlled timestamp.</summary>
    /// <param name="entry">Entry path and textual payload.</param>
    /// <param name="timestamp">Timestamp applied to the entry metadata.</param>
    /// <returns>A stream positioned at 0 and ready for read-mode ZIP access.</returns>
    private static MemoryStream BuildArchive((string Path, string Contents) entry, DateTimeOffset timestamp)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var zipEntry = archive.CreateEntry(entry.Path);
            zipEntry.LastWriteTime = timestamp;

            using var writer = zipEntry.Open();
            writer.Write(System.Text.Encoding.UTF8.GetBytes(entry.Contents));
        }

        stream.Position = 0;
        return stream;
    }
}
