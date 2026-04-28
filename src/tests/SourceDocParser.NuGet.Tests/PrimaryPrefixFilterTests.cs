// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins the primary-package filtering helpers on
/// <see cref="NuGetAssemblySource"/> — the change that stops the
/// walker from emitting documentation pages for transitively-pulled
/// runtime/platform assemblies (Microsoft.MAUI.Controls, Xamarin.*,
/// AndroidX.*, Kotlin.*) that came in via a primary's nuspec deps.
/// </summary>
public class PrimaryPrefixFilterTests
{
    /// <summary>
    /// The prefix array lays out IDs as bare-id / id+dot pairs so
    /// the filter handles umbrella DLL exact matches alongside
    /// sibling prefix matches in a single pass.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildPrimaryPrefixesEmitsBareAndDottedPair()
    {
        var config = new PackageConfig(
            NugetPackageOwners: [],
            TfmPreference: [],
            AdditionalPackages: [new("Splat", null), new("ReactiveUI", null)],
            ExcludePackages: [],
            ExcludePackagePrefixes: [],
            ReferencePackages: [],
            TfmOverrides: []);

        var prefixes = NuGetAssemblySource.BuildPrimaryPrefixes(config);

        await Assert.That(prefixes).IsEquivalentTo((string[])["Splat", "Splat.", "ReactiveUI", "ReactiveUI."]);
    }

    /// <summary>
    /// An empty <c>additionalPackages</c> list returns an empty
    /// prefix array — which <see cref="NuGetAssemblySource.IsPrimaryDll"/>
    /// reads as "no filter, walk everything" so owner-discovery and
    /// no-config flows aren't broken.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildPrimaryPrefixesReturnsEmptyForNoAdditionals()
    {
        var config = new PackageConfig(
            NugetPackageOwners: [],
            TfmPreference: [],
            AdditionalPackages: [],
            ExcludePackages: [],
            ExcludePackagePrefixes: [],
            ReferencePackages: [],
            TfmOverrides: []);

        var prefixes = NuGetAssemblySource.BuildPrimaryPrefixes(config);

        await Assert.That(prefixes.Length).IsEqualTo(0);
    }

    /// <summary>
    /// Bare-ID exact match: the umbrella DLL (<c>Splat.dll</c>)
    /// matches the bare <c>Splat</c> entry — case-insensitive.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllMatchesBareIdExactly()
    {
        string[] prefixes = ["Splat", "Splat.", "ReactiveUI", "ReactiveUI."];

        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Splat", prefixes)).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("splat", prefixes)).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("ReactiveUI", prefixes)).IsTrue();
    }

    /// <summary>
    /// Sibling assemblies (<c>Splat.Core.dll</c>, <c>Splat.Logging.dll</c>)
    /// match the dotted prefix entry — that's how the umbrella's
    /// <c>[TypeForwardedTo]</c> targets get their own pages.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllMatchesDottedSiblings()
    {
        string[] prefixes = ["Splat", "Splat.", "ReactiveUI", "ReactiveUI."];

        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Splat.Core", prefixes)).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Splat.Logging", prefixes)).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("ReactiveUI.AndroidX.UiToolkit", prefixes)).IsTrue();
    }

    /// <summary>
    /// Transitive DLLs that don't share a primary's prefix
    /// (<c>Microsoft.Maui.Controls.dll</c>, <c>Xamarin.Google.*</c>,
    /// <c>System.Reactive.dll</c>) get filtered out — they stay on
    /// disk for the compilation's fallback index but the walker
    /// never visits them.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllRejectsUnrelatedDlls()
    {
        string[] prefixes = ["Splat", "Splat.", "ReactiveUI", "ReactiveUI."];

        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Microsoft.Maui.Controls", prefixes)).IsFalse();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Xamarin.Google.Crypto.Tink", prefixes)).IsFalse();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("System.Reactive", prefixes)).IsFalse();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("AndroidX.Core", prefixes)).IsFalse();
    }

    /// <summary>
    /// An empty prefix array means "no filter configured" — every
    /// DLL passes. Mirrors the behaviour of the source when the
    /// config has no <c>additionalPackages</c>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllReturnsTrueWhenNoPrefixes()
    {
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Anything", [])).IsTrue();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("Microsoft.Maui.Controls", [])).IsTrue();
    }

    /// <summary>
    /// A near-miss (<c>Splat</c> is primary, <c>SplatExtra</c> is
    /// some unrelated package) doesn't accidentally pass: the
    /// dotted prefix is required for sibling matching, not just
    /// "starts with".
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsPrimaryDllRejectsNearMissNames()
    {
        string[] prefixes = ["Splat", "Splat."];

        await Assert.That(NuGetAssemblySource.IsPrimaryDll("SplatExtra", prefixes)).IsFalse();
        await Assert.That(NuGetAssemblySource.IsPrimaryDll("SplatLike", prefixes)).IsFalse();
    }

    /// <summary>
    /// The id-list overload (used to consume the fetcher-written
    /// sidecar) emits the same bare / dotted layout as the
    /// PackageConfig-driven path so owner-discovered ids reach the
    /// walker filter unchanged.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildPrimaryPrefixesFromIdsEmitsBareAndDottedPair()
    {
        var prefixes = NuGetAssemblySource.BuildPrimaryPrefixesFromIds(["Splat", "ReactiveUI"]);

        await Assert.That(prefixes).IsEquivalentTo((string[])["Splat", "Splat.", "ReactiveUI", "ReactiveUI."]);
    }

    /// <summary>
    /// Null / empty entries are skipped — the helper allocates an
    /// exact-sized array sized off the surviving id count so a
    /// hand-edited sidecar with stray blank lines doesn't leak nulls
    /// into the prefix array.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildPrimaryPrefixesFromIdsSkipsBlankEntries()
    {
        var prefixes = NuGetAssemblySource.BuildPrimaryPrefixesFromIds([string.Empty, "Splat", null!, "ReactiveUI"]);

        await Assert.That(prefixes).IsEquivalentTo((string[])["Splat", "Splat.", "ReactiveUI", "ReactiveUI."]);
    }

    /// <summary>
    /// Empty input returns an empty array (no allocation surprises) so
    /// the walker fallback to "no filter, walk everything" still
    /// triggers when the sidecar exists but is empty.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildPrimaryPrefixesFromIdsEmptyForBlankInput()
    {
        await Assert.That(NuGetAssemblySource.BuildPrimaryPrefixesFromIds([]).Length).IsEqualTo(0);
        await Assert.That(NuGetAssemblySource.BuildPrimaryPrefixesFromIds([string.Empty, null!]).Length).IsEqualTo(0);
    }

    /// <summary>
    /// The sidecar reader trims whitespace, ignores blank lines and
    /// <c>#</c>-prefixed comments. Mirrors the format the fetcher
    /// writes — one id per line, in declaration order.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadPrimaryIdsSidecarParsesIdsAndIgnoresCommentsAndBlanks()
    {
        var sidecar = Path.Combine(Path.GetTempPath(), $"primary-packages-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(sidecar, "# header line\n\nReactiveUI\n  Splat  \n# another comment\nSystem.Reactive\n");
        try
        {
            var ids = NuGetAssemblySource.ReadPrimaryIdsSidecar(sidecar);

            await Assert.That(ids).IsEquivalentTo((string[])["ReactiveUI", "Splat", "System.Reactive"]);
        }
        finally
        {
            File.Delete(sidecar);
        }
    }

    /// <summary>
    /// <see cref="NuGetAssemblySource.ResolvePrimaryPrefixes"/> prefers
    /// the sidecar over the manifest — the bug fix's contract. When
    /// both files exist on disk, the owner-discovered ids in the
    /// sidecar win over the (smaller) additionalPackages list.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolvePrimaryPrefixesPrefersSidecarOverManifest()
    {
        var sidecar = Path.Combine(Path.GetTempPath(), $"primary-{Guid.NewGuid():N}.txt");
        var manifest = Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(sidecar, "ReactiveUI\nSplat\n");
        await File.WriteAllTextAsync(manifest, "{\"additionalPackages\":[{\"id\":\"DynamicData\"}]}");
        try
        {
            var prefixes = NuGetAssemblySource.ResolvePrimaryPrefixes(sidecar, manifest);

            await Assert.That(prefixes).IsEquivalentTo((string[])["ReactiveUI", "ReactiveUI.", "Splat", "Splat."]);
        }
        finally
        {
            File.Delete(sidecar);
            File.Delete(manifest);
        }
    }

    /// <summary>
    /// Falls back to the manifest's additionalPackages when no
    /// sidecar is present — preserves backwards compatibility with
    /// hand-populated apiPaths and integration tests that don't go
    /// through the fetcher.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolvePrimaryPrefixesFallsBackToManifestWhenSidecarMissing()
    {
        var sidecar = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        var manifest = Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(manifest, "{\"additionalPackages\":[{\"id\":\"DynamicData\"}]}");
        try
        {
            var prefixes = NuGetAssemblySource.ResolvePrimaryPrefixes(sidecar, manifest);

            await Assert.That(prefixes).IsEquivalentTo((string[])["DynamicData", "DynamicData."]);
        }
        finally
        {
            File.Delete(manifest);
        }
    }

    /// <summary>
    /// Returns an empty array when neither file exists — caller's
    /// <see cref="NuGetAssemblySource.IsPrimaryDll"/> reads that as
    /// "no filter configured, walk everything".
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolvePrimaryPrefixesReturnsEmptyWhenBothMissing()
    {
        var sidecar = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        var manifest = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");

        var prefixes = NuGetAssemblySource.ResolvePrimaryPrefixes(sidecar, manifest);

        await Assert.That(prefixes.Length).IsEqualTo(0);
    }
}
